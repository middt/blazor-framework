# İş Akışı Tabloları Bölümleme (Partitioning) ve İndeksleme Kılavuzu

Bu belge, Middt Framework Workflow Engine içerisindeki tüm veritabanı tablolarının (`WorkflowInstances`, `WorkflowInstanceData`, `WorkflowHistory`, `WorkflowJobs`, `WorkflowIncidents`) PostgreSQL üzerinde **Declarative Partitioning (Flow Bazlı Liste Bölümleme)** ve **akışa özel indeksleme** stratejileriyle nasıl ölçeklendirileceğini, yapılandırılacağını ve optimize edileceğini açıklar.

---

## 1. Mimari Genel Bakış

Middt Framework'te iş akışı motoru 5 ana tablodan oluşur ve tüm tablolar **`WorkflowKey`** etrafında birincil sınıf vatandaş olarak tasarlanmıştır:

| Tablo | Sorumluluk | Anahtar (PK) | Partition Tipi | Büyüme Hızı |
| :--- | :--- | :---: | :---: | :---: |
| **`WorkflowInstances`** | Akışın durum ve yaşam döngüsü metadata'sı (`State`, `Status`, `BusySince`, `Revision`, `BackgroundTaskId`, `HistoryPatchCount`, `Audit`) | `(Id, WorkflowKey)` | `LIST (WorkflowKey)` | Orta |
| **`WorkflowInstanceData`** | Akışın tüm JSON iş verisi (`Data` column `jsonb`, `DataVersion`, `DataHash`, `ETag`) | `(InstanceId, WorkflowKey)` | `LIST (WorkflowKey)` | Hızlı (Büyük payload) |
| **`WorkflowHistory`** | Her geçişin audit kaydı. `history: full` modunda geçiş anındaki JSON snapshot (`Data`), `delta` modunda çoğunlukla yalnızca değişiklik (`Patch`, JSON Patch) ve arada snapshot'lar; `none` modunda satır yazılmaz | `(Id, WorkflowKey)` | `LIST (WorkflowKey)` | Çok Hızlı (En büyük tablo) |
| **`WorkflowJobs`** | Zamanlanmış geçişler, asenkron geçişler, zaman aşımları, bekletilen mesajlar (`Message`) ve arka plan görevleri (`Task`); PostgreSQL'de `FOR UPDATE SKIP LOCKED` ile alınır | `(Id, WorkflowKey)` | `LIST (WorkflowKey)` / Queue | Dinamik (Geçici kuyruk) |
| **`WorkflowIncidents`** | Akış sırasında oluşan hatalar, retry denemeleri ve incident kayıtları | `(Id, WorkflowKey)` | `LIST (WorkflowKey)` | Düşük/Orta |

### Neden Tüm Tablolarda Bileşik Anahtar: `(..., WorkflowKey)`?
PostgreSQL kuralları gereği, bir tablo bölümlendiğinde (partitioned table):
1. **Birincil Anahtar (Primary Key)** ve **Tekil İndeksler (Unique Indexes)** mutlaka bölümleme anahtarını (`WorkflowKey`) içermek zorundadır.
2. Tablolar arası **Foreign Key (Yabancı Anahtar)** ilişkileri kurulurken, ana tablo bölümlenmişse alt tablonun yabancı anahtarı da `(InstanceId, WorkflowKey)` şeklinde bileşik olmalıdır.

Bu mimari sayesinde PostgreSQL:
- **Partition-Wise Join:** `WorkflowInstances` ile `WorkflowInstanceData` veya `WorkflowHistory` tablolarını birleştirirken (join), her iki tablonun da aynı flow partition'larını doğrudan eşleştirir. Diğer flow'ların verilerine hiç dokunmaz.
- **Partition Pruning:** Bir sorgu `WHERE "WorkflowKey" = 'order-payment-orchestration'` içerdiğinde, PostgreSQL 5 tablonun tamamında yalnızca sipariş akışının partition'larını tarar.

---

## 2. PostgreSQL DDL Şablonu (Tüm Tablolar)

Aşağıdaki SQL şablonu, 5 tablonun tamamını root partitioned tablo ve `DEFAULT` güvenlik ağı partition'larıyla birlikte tanımlar:

```sql
CREATE SCHEMA IF NOT EXISTS wf;

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. WorkflowInstances (Durum & Yaşam Döngüsü)
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS wf."WorkflowInstances" (
    "Id" uuid NOT NULL,
    "WorkflowKey" varchar(128) NOT NULL,
    "WorkflowVersion" int NOT NULL,
    "BusinessKey" varchar(256),
    "State" varchar(128) NOT NULL,
    "Status" varchar(16) NOT NULL,
    "PendingTransition" varchar(128),
    "PendingInput" text,
    "StartedBy" varchar(256),
    "LastExecutedBy" varchar(256),
    "Revision" int NOT NULL DEFAULT 0,
    "BusySince" timestamptz,
    "ParentInstanceId" uuid,
    "WaitingForInstanceId" uuid,
    "Error" text,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz NOT NULL,
    "CompletedAt" timestamptz,
    "BackgroundTaskId" uuid,       -- beklenen arka plan görevinin job id'si
    "HistoryPatchCount" int,       -- history: delta – son snapshot'tan beri yazılan patch sayısı
    CONSTRAINT "PK_WorkflowInstances" PRIMARY KEY ("Id", "WorkflowKey")
) PARTITION BY LIST ("WorkflowKey");

CREATE TABLE IF NOT EXISTS wf."WorkflowInstances_Default" 
    PARTITION OF wf."WorkflowInstances" DEFAULT;

CREATE INDEX IF NOT EXISTS "IX_WorkflowInstances_WorkflowKey_BusinessKey" 
    ON wf."WorkflowInstances" ("WorkflowKey", "BusinessKey");
CREATE INDEX IF NOT EXISTS "IX_WorkflowInstances_WorkflowKey_CreatedAt" 
    ON wf."WorkflowInstances" ("WorkflowKey", "CreatedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_WorkflowInstances_Status_BusySince" 
    ON wf."WorkflowInstances" ("Status", "BusySince");

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. WorkflowInstanceData (JSON Veri Yükü)
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS wf."WorkflowInstanceData" (
    "InstanceId" uuid NOT NULL,
    "WorkflowKey" varchar(128) NOT NULL,
    "Data" jsonb NOT NULL,
    "DataVersion" int NOT NULL DEFAULT 0,
    "DataHash" varchar(64) NOT NULL,
    "ETag" varchar(64) NOT NULL,
    "UpdatedAt" timestamptz NOT NULL,
    CONSTRAINT "PK_WorkflowInstanceData" PRIMARY KEY ("InstanceId", "WorkflowKey"),
    CONSTRAINT "FK_WorkflowInstanceData_Instances" FOREIGN KEY ("InstanceId", "WorkflowKey") 
        REFERENCES wf."WorkflowInstances" ("Id", "WorkflowKey") ON DELETE CASCADE
) PARTITION BY LIST ("WorkflowKey");

CREATE TABLE IF NOT EXISTS wf."WorkflowInstanceData_Default" 
    PARTITION OF wf."WorkflowInstanceData" DEFAULT;

CREATE INDEX IF NOT EXISTS "IX_WorkflowInstanceData_DataHash" 
    ON wf."WorkflowInstanceData" ("DataHash");
CREATE INDEX IF NOT EXISTS "IX_WorkflowInstanceData_WorkflowKey_UpdatedAt" 
    ON wf."WorkflowInstanceData" ("WorkflowKey", "UpdatedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_WorkflowInstanceData_Data_GIN" 
    ON wf."WorkflowInstanceData" USING gin ("Data" jsonb_path_ops);

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. WorkflowHistory (Geçiş & Denetim İzleri)
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS wf."WorkflowHistory" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
    "InstanceId" uuid NOT NULL,
    "WorkflowKey" varchar(128) NOT NULL,
    "TransitionKey" varchar(128) NOT NULL,
    "FromState" varchar(128),
    "ToState" varchar(128) NOT NULL,
    "Trigger" varchar(16) NOT NULL,
    "ExecutedBy" varchar(256),
    "Input" jsonb,
    "DataVersion" int NOT NULL,
    "Data" jsonb,                  -- snapshot (full modda her satır, delta modda yalnızca checkpoint satırları)
    "Tasks" jsonb,
    "StartedAt" timestamptz NOT NULL,
    "FinishedAt" timestamptz NOT NULL,
    "Patch" jsonb,                 -- history: delta – önceki sürüme göre değişiklik (RFC 6902 JSON Patch, örnekler: README "History modes")
    CONSTRAINT "PK_WorkflowHistory" PRIMARY KEY ("Id", "WorkflowKey")
) PARTITION BY LIST ("WorkflowKey");

CREATE TABLE IF NOT EXISTS wf."WorkflowHistory_Default" 
    PARTITION OF wf."WorkflowHistory" DEFAULT;

CREATE INDEX IF NOT EXISTS "IX_WorkflowHistory_WorkflowKey_InstanceId" 
    ON wf."WorkflowHistory" ("WorkflowKey", "InstanceId");

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. WorkflowJobs (Zamanlanmış ve Asenkron İşler)
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS wf."WorkflowJobs" (
    "Id" uuid NOT NULL,
    "InstanceId" uuid NOT NULL,
    "WorkflowKey" varchar(128) NOT NULL,
    "Kind" varchar(16) NOT NULL,
    "TransitionKey" varchar(128),
    "State" varchar(128),
    "Payload" jsonb,
    "RequestedBy" varchar(256),
    "DueAt" timestamptz NOT NULL,
    "LockedUntil" timestamptz,
    "Attempts" int NOT NULL DEFAULT 0,
    "LastError" text,
    "CreatedAt" timestamptz NOT NULL,
    CONSTRAINT "PK_WorkflowJobs" PRIMARY KEY ("Id", "WorkflowKey")
) PARTITION BY LIST ("WorkflowKey");

CREATE TABLE IF NOT EXISTS wf."WorkflowJobs_Default" 
    PARTITION OF wf."WorkflowJobs" DEFAULT;

CREATE INDEX IF NOT EXISTS "IX_WorkflowJobs_DueAt" 
    ON wf."WorkflowJobs" ("DueAt");
CREATE INDEX IF NOT EXISTS "IX_WorkflowJobs_WorkflowKey_DueAt" 
    ON wf."WorkflowJobs" ("WorkflowKey", "DueAt");

-- İşler node'lar arasında böyle alınır: kilitli satır atlanır, kimse kimseyi beklemez.
-- UPDATE wf."WorkflowJobs" AS j SET "LockedUntil" = @lease, "Attempts" = j."Attempts" + 1
-- FROM (SELECT "Id", "WorkflowKey" FROM wf."WorkflowJobs"
--       WHERE "DueAt" <= @now AND ("LockedUntil" IS NULL OR "LockedUntil" < @now)
--       ORDER BY "DueAt" LIMIT 1 FOR UPDATE SKIP LOCKED) AS next
-- WHERE j."Id" = next."Id" AND j."WorkflowKey" = next."WorkflowKey" RETURNING j."Id";
-- Job yazan node NOTIFY middt_wf_jobs_<şema> gönderir; diğer node'lar LISTEN ile hemen uyanır.

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. WorkflowIncidents (Hata & Retry Kayıtları)
-- ─────────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS wf."WorkflowIncidents" (
    "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
    "InstanceId" uuid NOT NULL,
    "WorkflowKey" varchar(128) NOT NULL,
    "State" varchar(128),
    "TransitionKey" varchar(128),
    "TaskKey" varchar(128),
    "Error" text NOT NULL,
    "Attempts" int NOT NULL DEFAULT 1,
    "CreatedAt" timestamptz NOT NULL,
    "ResolvedAt" timestamptz,
    CONSTRAINT "PK_WorkflowIncidents" PRIMARY KEY ("Id", "WorkflowKey")
) PARTITION BY LIST ("WorkflowKey");

CREATE TABLE IF NOT EXISTS wf."WorkflowIncidents_Default" 
    PARTITION OF wf."WorkflowIncidents" DEFAULT;

CREATE INDEX IF NOT EXISTS "IX_WorkflowIncidents_WorkflowKey_InstanceId" 
    ON wf."WorkflowIncidents" ("WorkflowKey", "InstanceId");
```

---

## 3. Dedicated Partition Ekleme (Örnek Akış)

Yeni ve yüksek hacimli bir akış (örneğin `order-payment-orchestration`) devreye alındığında, 5 tablo için dedicated partition'lar oluşturulur:

```sql
-- 1. Instances
CREATE TABLE IF NOT EXISTS wf."WorkflowInstances_OrderPayment" 
    PARTITION OF wf."WorkflowInstances" 
    FOR VALUES IN ('order-payment-orchestration');

-- 2. Data
CREATE TABLE IF NOT EXISTS wf."WorkflowInstanceData_OrderPayment" 
    PARTITION OF wf."WorkflowInstanceData" 
    FOR VALUES IN ('order-payment-orchestration');

-- 3. History
CREATE TABLE IF NOT EXISTS wf."WorkflowHistory_OrderPayment" 
    PARTITION OF wf."WorkflowHistory" 
    FOR VALUES IN ('order-payment-orchestration');

-- 4. Jobs
CREATE TABLE IF NOT EXISTS wf."WorkflowJobs_OrderPayment" 
    PARTITION OF wf."WorkflowJobs" 
    FOR VALUES IN ('order-payment-orchestration');

-- 5. Incidents
CREATE TABLE IF NOT EXISTS wf."WorkflowIncidents_OrderPayment" 
    PARTITION OF wf."WorkflowIncidents" 
    FOR VALUES IN ('order-payment-orchestration');
```

> [!NOTE]
> **Otomatik Güvenlik:** Eğer bu SQL çalıştırılmadan önce `order-payment-orchestration` başlatılırsa, kayıtlar otomatik olarak `_Default` partition'larına düşer. Sistem kesintiye uğramaz.

---

## 4. Akışa Özel JSONB İndeksleri

Dedicated partition açıldıktan sonra, `WorkflowInstanceData_OrderPayment` üzerinde doğrudan B-Tree fonksiyonel indeksleri tanımlanır:

```sql
-- Müşteri ID araması
CREATE INDEX IF NOT EXISTS "IX_Part_Order_CustomerId" 
ON wf."WorkflowInstanceData_OrderPayment" ((("Data"->>'customerId')::text));

-- Tutar sıralaması ve aralık sorguları
CREATE INDEX IF NOT EXISTS "IX_Part_Order_Total" 
ON wf."WorkflowInstanceData_OrderPayment" (((("Data"->>'total')::numeric)));

-- Sipariş durum kodu
CREATE INDEX IF NOT EXISTS "IX_Part_Order_StatusCode" 
ON wf."WorkflowInstanceData_OrderPayment" ((("Data"->>'statusCode')::text));
```

---

## 5. Operasyonel Avantajlar ve Yaşam Döngüsü Yönetimi

1. **Partition-Wise Join Performansı:**
   `WorkflowInstances` ile `WorkflowInstanceData` join edildiğinde PostgreSQL yalnızca `WorkflowInstances_OrderPayment` ile `WorkflowInstanceData_OrderPayment` tablolarını birleştirir; veri eşleştirmesi doğrudan CPU önbelleğinde tamamlanır.
2. **Sıfır Kesintiyle Arşivleme:**
   Eski sipariş geçmişini arşivlemek için devasa tablolarda milyonlarca satırı `DELETE` edip diski kilitlemek yerine:
   ```sql
   ALTER TABLE wf."WorkflowHistory" DETACH PARTITION wf."WorkflowHistory_OrderPayment_2024";
   ```
   komutuyla eski yılın partition'ı bir milisaniyede ana tablodan ayrılır ve soğuk depoya taşınır.
3. **İzole Bakım:**
   `VACUUM (ANALYZE)` veya `REINDEX` işlemleri yoğun çalışan partition'lara ayrı ayrı uygulanabilir.
