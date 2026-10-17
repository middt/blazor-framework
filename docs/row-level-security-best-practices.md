# Best Practice: Satır Bazlı Güvenlik ve Şube Yetkilendirme Rehberi

Bu rehber; bankacılık, sigortacılık veya çok şubeli/kurumsal yapılarda **Satır Bazlı Güvenlik (Row-Level Security - RLS)**, **Kayıt Bazlı Yetkilendirme (Record-Level Authorization)** ve **İş Akışlarında Şube İzolasyonunu** uçtan uca uygulamak için en iyi pratikleri sunar.

---

## 1. Mimari Katmanlar

Kurumsal uygulamalarda güvenlik tek bir noktaya (yalnızca UI veya yalnızca controller) bırakılamaz. Savunma derinliği (**Defense in Depth**) ilkesiyle 4 katmanda ele alınır:

```
┌────────────────────────────────────────────────────────┐
│ 1. Frontend & UI (Server-Driven Layouts & Workflows)    │
│    - Şubeye göre filtrelenmiş dropdown/picker seçimleri │
│    - Yetkisiz butonların gizlenmesi (visibleWhen)       │
├────────────────────────────────────────────────────────┤
│ 2. Workflow Engine (İş Akışı Katmanı)                  │
│    - Transition kuralları: roles + rule                 │
│    - Şube müdürü yetki doğrulaması                      │
├────────────────────────────────────────────────────────┤
│ 3. API & Controller (REST Endpoint'leri)               │
│    - ApplyAccessFilter (Grid ve liste sorguları)       │
│    - AuthorizeAsync (Tekil kayıt / IDOR koruması)       │
│    - Mass-assignment koruması ([Editable(false)])       │
├────────────────────────────────────────────────────────┤
│ 4. Veritabanı & EF Core (Global Query Filters)         │
│    - HasQueryFilter (SQL seviyesinde otomatik WHERE)    │
│    - İstemci veya geliştirici unutsun, veri sızamaz     │
└────────────────────────────────────────────────────────┘
```

---

## 2. Kullanıcı Şube Bağlamı (`ICurrentBranchContext`)

Kullanıcının şube bilgisi kimlik doğrulama token'ından (OIDC / JWT Claim) okunur. Tüm katmanların erişebileceği hafif ve test edilebilir bir servis tanımlanır:

```csharp
public interface ICurrentBranchContext
{
    int? BranchId { get; }
    string? UserId { get; }
    bool IsHeadquarters { get; }
}

public sealed class CurrentBranchContext(IHttpContextAccessor httpContextAccessor) : ICurrentBranchContext
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public int? BranchId =>
        int.TryParse(User?.FindFirst("branch_id")?.Value, out var id) ? id : null;

    public string? UserId =>
        User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst(ClaimTypes.Sid)?.Value;

    public bool IsHeadquarters =>
        User?.IsInRole("Headquarters") == true || User?.IsInRole("Auditor") == true;
}
```

---

## 3. Katman 1: Veritabanı Seviyesi (EF Core Global Query Filters)

Geliştirici yeni bir sorgu yazsa, `Include` ile ilişkili tabloya atlasa dahi başka şubenin verisinin sorguya karışmasını engellemek için `DbContext.OnModelCreating` içerisinde **Global Query Filter** tanımlanır:

```csharp
public class BankingDbContext(
    DbContextOptions<BankingDbContext> options,
    ICurrentBranchContext branchContext) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Müşteri Tablosu
        modelBuilder.Entity<Customer>().HasQueryFilter(c =>
            branchContext.IsHeadquarters || 
            (branchContext.BranchId.HasValue && c.BranchId == branchContext.BranchId.Value));

        // 2. Müşteri Hesapları Tablosu
        modelBuilder.Entity<Account>().HasQueryFilter(a =>
            branchContext.IsHeadquarters || 
            (branchContext.BranchId.HasValue && a.BranchId == branchContext.BranchId.Value));
    }
}
```

> **Önemli Kural:** Global filtreler Genel Müdürlük / Teftiş kullanıcıları için `branchContext.IsHeadquarters` ile şeffaf şekilde bypass edilebilir.

---

## 4. Katman 2: API & Controller Seviyesi

Middt Framework'ün [BaseListController](file:///Volumes/Data/personel/github/blazor-framework/sdk/server/Middt.Framework.Api/BaseController/BaseListController.cs) ve [BaseCrudController](file:///Volumes/Data/personel/github/blazor-framework/sdk/server/Middt.Framework.Api/BaseController/BaseCrudController.cs) sınıfları satır bazlı güvenlik için iki ana kanca sunar:

### A. Liste Sorguları Filtresi: `ApplyAccessFilter`
`getall` ve `getitems` (sayfalama, sıralama, arama) endpoint'lerinde çalıştırılan IQueryable zincirine otomatik eklenir:

```csharp
[Authorize]
public class CustomerController(
    CustomerRepository repository,
    ICurrentBranchContext branchContext)
    : BaseCrudControllerWithAuth<Customer, CustomerRepository>(repository)
{
    protected override IQueryable<Customer> ApplyAccessFilter(IQueryable<Customer> query)
    {
        if (branchContext.IsHeadquarters)
            return query;

        return query.Where(c => c.BranchId == branchContext.BranchId);
    }
}
```

### B. Tekil Kayıt Yetkilendirmesi (IDOR Koruması): `AuthorizeAsync`
Bir kullanıcı URL'e rastgele bir müşteri ID'si yazıp çağırdığında (`/api/v1/customer/getbyid?id=9876`):

```csharp
protected override Task<bool> AuthorizeAsync(Customer customer, CrudOperation operation, CancellationToken ct)
{
    if (branchContext.IsHeadquarters)
        return Task.FromResult(true);

    // Başka şubenin müşterisi ise false döner
    return Task.FromResult(customer.BranchId == branchContext.BranchId);
}
```

> **Altın Kural (404 vs 403):** 
> Framework, yetkisiz `GetById`, `Update` ve `Delete` isteklerinde kasıtlı olarak **`404 Not Found`** döner. Saldırgan `403 Forbidden` aldığında "Demek ki 9876 ID'li müşteri var ama yetkim yok" diyerek müşteri ID'lerini tarayabilir (ID Enumeration). `404` döndüğünde kaydın var olup olmadığını bilemez.

### C. Kütle Atama (Mass-Assignment) Koruması
Yeni müşteri oluştururken veya güncellerken istemcinin sahte bir `BranchId` göndermesini engellemek:

```csharp
public class Customer
{
    public int CustomerId { get; set; }

    [Editable(false)] // İstemciden gelen payload bu alanı asla ezemez
    public int BranchId { get; set; }
}
```

Controller'da zorlama:
```csharp
public override async Task<ActionResult<BaseResponseDataModel<Customer>>> Insert(
    [FromBody] Customer model, CancellationToken cancellationToken)
{
    if (!branchContext.IsHeadquarters)
    {
        model.BranchId = branchContext.BranchId ?? throw new UnauthorizedAccessException();
    }

    return await base.Insert(model, cancellationToken);
}
```

---

## 5. Katman 3: İş Akışlarında Şubeye Özel Dinamik Veri Okuma

> **Vaka:** Müşteri ekleme veya hesap açılış akışında, açılır kutudan (dropdown) **yalnızca o şubenin personelleri** seçilebilmelidir.

### Yöntem 1: Doğrudan SQL ile Yükleme (`db-query`)
Akışın ilgili adımına (`onEntry`) doğrudan bir `db-query` görevi eklenerek şube ID'si parametrik olarak sorgulanabilir:

```json
{
  "task": "db-query",
  "config": {
    "sql": "SELECT staff_id AS id, first_name + ' ' + last_name AS name FROM staffs WHERE branch_id = {0} AND active = 1",
    "parameters": [ "{{ input.branchId ?? data.branchId }}" ],
    "into": "availableStaffList"
  }
}
```

> 💡 **Not:** Parametre yalnızca akış verisinden (`data.branchId`) değil; kullanıcının formda gönderdiği istek gövdesinden (`input.branchId`), HTTP Header'dan veya QueryString'den de gelebilir. Aşağıda bu kaynakların nasıl yönetileceği detaylandırılmıştır.

> ⚠️ **Uyarı:** Aynı sorguyu birden fazla iş akışında tekrar tekrar yazmak (kredi, müşteri ekleme, hesap açma) **kod tekrarına (DRY ihlali)** ve şema değiştiğinde tüm JSON dosyalarını güncelleme yüküne yol açar. Bu nedenle aşağıdaki **SubFlow (Alt Akış)** deseni tercih edilmelidir.

---

### Yöntem 2: Tekrar Kullanılabilir SubFlow (Alt Akış) Deseni *(Önerilen Yaklaşım)*

SQL sorgusunu her akışa gömmek yerine, veri çekme mantığı **bağımsız ve tekrar kullanılabilir bir Alt Akış (SubFlow)** haline getirilir:

#### 1. Reusable Alt Akış Tanımı (`get-branch-staff.json`)
Bu akış parametre olarak `branchId` alır, doğrular, sorguyu çalıştırır ve sonucu döner:

```json
{
  "key": "get-branch-staff",
  "version": 1,
  "title": "Şube Personel Listesini Getir",
  "start": { "key": "start", "target": "validate" },
  "states": [
    {
      "key": "validate",
      "title": "Girdi Doğrulama",
      "transitions": [
        { "key": "valid", "target": "query", "trigger": "Automatic", "rule": "data.branchId != null && data.branchId > 0" },
        { "key": "invalid", "target": "failed", "trigger": "Automatic", "default": true }
      ]
    },
    {
      "key": "query",
      "title": "Personel Sorgusu",
      "onEntry": [
        {
          "task": "db-query",
          "config": {
            "sql": "SELECT staff_id AS id, first_name + ' ' + last_name AS name FROM staffs WHERE branch_id = {0} AND active = 1",
            "parameters": [ "{{ data.branchId }}" ],
            "into": "items"
          }
        }
      ],
      "transitions": [
        { "key": "done", "target": "completed", "trigger": "Automatic", "default": true }
      ]
    },
    { "key": "completed", "title": "Başarılı", "type": "Finish", "subType": "Success" },
    { "key": "failed", "title": "Geçersiz Şube", "type": "Finish", "subType": "Error" }
  ]
}
```

#### 2. Ebeveyn Akıştan Alt Akışın Çağrılması
Herhangi bir ana iş akışında (müşteri açılışı, transfer onayı, vb.) tek satır SQL yazmadan bu alt akış bir adım olarak bağlanır:

```json
{
  "key": "load-staff",
  "title": "Şube Personelini Yükle",
  "type": "SubFlow",
  "subFlow": {
    "workflow": "get-branch-staff",
    "input": {
      "branchId": "data.branchId"
    },
    "outputInto": "availableStaff"
  },
  "transitions": [
    {
      "key": "staff-loaded",
      "target": "assign-staff-step",
      "trigger": "Automatic",
      "rule": "child.subType == \"Success\""
    },
    {
      "key": "load-failed",
      "target": "staff-load-error",
      "trigger": "Automatic",
      "default": true
    }
  ]
}
```

#### 3. Ekranda Verinin Kullanılması (`assign-staff-step`)
Alt akış tamamlandığında veriler ebeveyn akışın `data.availableStaff` alanına yazılır. Formdaki açılır kutu doğrudan bu veriye bağlanır:

```json
{
  "key": "assign-staff-step",
  "title": "Müşteri Temsilcisi Atama",
  "view": {
    "type": "Form",
    "schema": {
      "type": "object",
      "properties": {
        "selectedStaffId": {
          "type": "integer",
          "title": "Atanan Müşteri Temsilcisi",
          "enumSource": "data.availableStaff.items"
        }
      },
      "required": [ "selectedStaffId" ]
    }
  },
  "transitions": [
    { "key": "submit-staff", "target": "next-step", "trigger": "Manual" }
  ]
}
```

#### Neden SubFlow Deseni?
1. **Sıfır Bekleme Süresi (Senkron Yürütme):** Alt akışın tüm adımları `Automatic` olduğunda, Middt motoru alt akışı başlatır, sorguyu çalıştırır ve ebeveyne geri döner. Ebeveyn akış tek bir tick içinde `assign-staff-step` ekranına geçer; kullanıcı gecikme hissetmez.
2. **Kapsülleme (Encapsulation):** Tablo adı, kolon adı veya filtre kuralı (`active = 1`, `store_id`) değiştiğinde yalnızca `get-branch-staff.json` dosyası güncellenir; onlarca iş akışına dokunulmaz.
3. **Versiyonlama Desteği:** İleride `version: 2` çıkartılabilir ve eski çalışan akışlar bozulmadan yeni akışlara entegre edilebilir (`"version": 2`).
4. **Kolay Zenginleştirme:** Gelecekte alt akışın içine SQL sorgusu öncesinde Redis önbellek kontrolü (`cache-get`) veya harici İK servisinden LDAP kontrolü (`http-request`) eklemek mümkündür.

---

### Parametre Kaynakları: Header, QueryString, Request Body ve JWT Claim'lerinin Çözümlenmesi

Gerçek hayat senaryolarında `branchId` gibi filtre veya iş parametreleri yalnızca akışın o anki durum verisinde (`data.branchId`) bulunmayabilir. Bilgi şu kaynaklardan biri üzerinden sisteme gelebilir:

1. **Request Body / Form Girdisi (`input`):** Kullanıcının ekrandaki formu doldurup "İlerle" butonuna bastığı an veya REST API üzerinden `POST /transitions/{transition}` gövdesinde gönderilen JSON verisi.
2. **HTTP Header (`X-Branch-Id`) veya JWT Claim:** Kimlik doğrulama token'ı (OIDC/OAuth2) içindeki kullanıcının bağlı olduğu şube (`branch_id`) veya kurumsal ağ geçidinden (API Gateway/BFF) gelen özel header.
3. **QueryString (`?branchId=101`):** Sayfa rotasında (URL) veya API çağrısında parametre olarak iletilen değer.
4. **Mevcut Akış Durumu (`data`):** Akışın önceki bir adımında kullanıcı tarafından seçilmiş veya veritabanından çekilip hafızada saklanan durum verisi.

Middt Framework bu farklı kaynakları şu stratejilerle destekler:

#### A. Otomatik HTTP Bağlamı (`input.header`, `input.query`, `input.body`)
Middt Framework'te REST API (`WorkflowEndpoints`) üzerinden gelen HTTP isteklerinde, framework gelen tüm HTTP bağlamını **otomatik olarak** `input` zarfına yerleştirir:

- **`input.header.*`**: İstek başlıkları (örneğin `X-Branch-Id: 101` gönderildiğinde `input.header.branchId`, `input.header.xBranchId` veya `input.header["X-Branch-Id"]`).
- **`input.query.*`**: URL sorgu parametreleri (örneğin `?branchId=101` gönderildiğinde `input.query.branchId`).
- **`input.body.*`**: İstek gövdesi (JSON body içerisindeki alanlar).
- **`input.*`**: Geriye dönük uyumluluk ve doğrudan erişim için gövdedeki alanlar (`body`) doğrudan `input` kökünde de mevcuttur (örneğin `input.branchId`).

> ⚡ **Otomatik ve Tip Güvenli:** Sayfa rotasında `?branchId=101` veya başlıkta `X-Branch-Id: 101` olduğunda **tek bir satır C# eşleme kodu yazmanıza gerek kalmaz**. Framework header ve query değerlerini otomatik olarak parse eder (sayı, boolean veya metin olarak) ve akış adımlarına aktarır.

#### B. İfade Düzeyinde Güvenli Fallback Zinciri (`??` Operatörü)
Middt'in taşınabilir ifade motoru (`ExpressionEvaluator`) **null-coalescing (`??`)** operatörünü destekler. Header ve query nesneleri her zaman güvenli boş nesne olarak sağlandığından `NullReferenceException` oluşmaz. İstediğiniz öncelik sırasına göre zincirleme yapabilirsiniz:

```json
{
  "task": "db-query",
  "config": {
    "sql": "SELECT staff_id AS id, first_name + ' ' + last_name AS name FROM staffs WHERE branch_id = {0} AND active = 1",
    "parameters": [
      "{{ input.header.branchId ?? input.query.branchId ?? input.body.branchId ?? data.branchId }}"
    ],
    "into": "availableStaffList"
  }
}
```

Alt akış çağrısında (`subFlow.input`) da aynı ifade doğrudan kullanılabilir:
```json
"subFlow": {
  "workflow": "get-branch-staff",
  "input": {
    "branchId": "input.header.branchId ?? input.query.branchId ?? input.body.branchId ?? data.branchId"
  },
  "outputInto": "availableStaff"
}
```
*Açıklama: HTTP başlığında şube varsa o kullanılır; yoksa URL query'deki şube; yoksa formdan girilen; o da yoksa akışın hafızasındaki şube devreye girer.*

#### C. Özel API Controller veya BFF Endpoint'lerinde Özel Müdahale (Opsiyonel)
Standart iş akışı uç noktaları (`/api/workflows/...`) yukarıdaki aktarımı otomatik olarak yaparken; eğer özel bir Controller üzerinden akış tetiklemek ve JWT token'daki `branch_id` gibi claim'leri zorunlu kılmak isterseniz:

```csharp
// Özel API Controller veya Minimal API Endpoint
app.MapPost("/api/branch-operations/{id}/assign-staff", async (
    Guid id,
    [FromHeader(Name = "X-Branch-Id")] long? headerBranchId,
    [FromQuery] long? queryBranchId,
    [FromBody] JsonObject input,
    WorkflowEngine engine,
    ClaimsPrincipal principal) =>
{
    // Güvenlik Öncelik Sıralaması:
    // 1. JWT Claim (Kullanıcının token'ındaki yetkili şubesi - en güvenli / manipüle edilemez)
    // 2. Framework'ün otomatik input.header veya input.query çözümü
    var branchId = principal.FindFirst("branch_id")?.Value
                   ?? headerBranchId?.ToString()
                   ?? queryBranchId?.ToString();

    if (branchId != null && !input.ContainsKey("branchId"))
    {
        input["branchId"] = long.Parse(branchId);
    }

    return await engine.ExecuteTransitionAsync("branch-operation", id, "submit", input, WorkflowUser.From(principal));
});
```

> 🔒 **Güvenlik Kuralı:** Eğer `branchId` kullanıcının görmeye yetkili olduğu veriyi sınırlıyorsa (Row-Level Security), istemciden gelen query string veya request body değerine **asla körü körüne güvenilmemelidir**. Token'daki yetkili şube claim'i (`user.Claims`) ile istemcinin talep ettiği şube uyuşmuyorsa istek `403 Forbidden` ile reddedilmelidir.

#### C. Önyüzden (Angular / Blazor) QueryString ve Context Aktarımı
Eğer kullanıcı sayfaya `?branchId=101` linki ile gelmişse veya aktif şubesi UI oturumunda saklanıyorsa:

- **Angular (`@middt/angular`):**
  ```typescript
  // Rota query parametresinden veya SessionService'ten okunur
  const branchId = this.route.snapshot.queryParamMap.get('branchId') ?? this.session.currentBranchId;

  // Geçiş tetiklenirken input'a eklenir
  this.workflowClient.executeTransition(workflow, instanceId, 'submit-store', {
    branchId: Number(branchId)
  });
  ```

- **Blazor (`Middt.Framework.Blazor.Web`):**
  ```csharp
  [SupplyParameterFromQuery]
  public long? BranchId { get; set; }

  // Geçiş çalıştırılırken aktarılır
  await WorkflowClient.ExecuteTransitionAsync(Workflow, instance.Id, "submit-store", new
  {
      branchId = BranchId ?? UserSession.CurrentBranchId
  });
  ```

#### D. Dış Servis Çağrılarında Header ve Query Aktarımı (`http-request` Task)
İş akışının bir adımı harici bir REST API çağrısı yapıyorsa (`HttpTask`), URL Query ve Header değerleri doğrudan JSON tanımı üzerinden `{{ ... }}` şablonlarıyla beslenebilir:

```json
{
  "task": "http-request",
  "config": {
    "method": "GET",
    "url": "https://api.internal/v1/staffs",
    "queryParams": {
      "branchId": "{{ input.branchId ?? data.branchId }}"
    },
    "headers": {
      "X-Branch-Id": "{{ input.branchId ?? data.branchId }}",
      "Authorization": "Bearer {{ data.accessToken }}"
    },
    "into": "availableStaff"
  }
}
```

---

### Yöntem 3: Dinamik Lookup (`x-lookup` + Controller Satır Filtresi)
Form şemasında `x-lookup` kullanıldığında:
```json
"staffId": {
  "type": "integer",
  "title": "Personel Seçiniz",
  "x-lookup": {
    "entity": "Staff",
    "valueField": "staffId",
    "textField": "name"
  }
}
```
Önyüz otomatik olarak `/api/staff/getall` çağrısı yapar. `StaffController` üzerindeki `ApplyAccessFilter` zaten kullanıcının şubesini bildiği için dönen liste **doğal olarak sadece o şubenin personellerini** içerir.

---

## 6. Katman 4: İş Akışlarında Şubeye Özel Onay Yetkisi

> **Vaka:** Müşteri 750.000 TL tutarında yüksek bir EFT yaptı. Bu transferin onayını **yalnızca işlemi yapan şubenin müdürü** verebilmelidir. Başka bir şubenin müdürü veya gişe görevlisi bu onayı veremez.

### A. İş Akışı Tanımında `roles` + `rule` Korunması

İş akışı JSON tanımında onay geçişine (transition) hem rol hem de şube eşleşme kuralı yazılır:

```json
{
  "key": "approve-high-value-eft",
  "title": "Yüksek Tutarlı EFT Onayla",
  "target": "eft-processed",
  "trigger": "Manual",
  "roles": [ "branch-manager" ],
  "rule": "user.isInRole('branch-manager') && user.branchId == data.branchId"
}
```

#### Bu Kuralın Sağladığı Güvenlik:
1. **Ekran Seviyesi:** Kadıköy Şube Müdürü (`user.branchId == 101`) Kadıköy'de açılan EFT'yi (`data.branchId == 101`) incelediğinde onay butonunu görür. Beşiktaş Şube Müdürü (`user.branchId == 102`) aynı ekrana baksa dahi **onay butonu ekranda görünmez**.
2. **API Seviyesi:** Beşiktaş Şube Müdürü API üzerinden doğrudan tetiklemeye kalksa dahi (`POST /api/workflows/eft-approval/instances/{id}/transitions/approve-high-value-eft`), `WorkflowEngine` transition kuralını doğrular ve isteği **409 Conflict / 403 Forbidden** ile reddeder.

### B. Tutar Limitine Göre Akıllı Yönlendirme (Automatic Transitions)
Limit aşımlarında Genel Müdürlük ya da Şube Müdürü ayrımı otomatik adımlarla dallandırılabilir:

```json
{
  "key": "route-by-amount",
  "title": "Onay Seviyesi Belirleme",
  "transitions": [
    {
      "key": "to-branch-mgr",
      "target": "pending-branch-manager",
      "trigger": "Automatic",
      "rule": "data.amount <= 1000000"
    },
    {
      "key": "to-hq-approval",
      "target": "pending-headquarters",
      "trigger": "Automatic",
      "rule": "data.amount > 1000000",
      "default": true
    }
  ]
}
```

---

## 7. Katman 5: Onay Havuzu ve Görev Listesi (Worklist / Inbox)

Şube müdürü sisteme girdiğinde tüm bankadaki binlerce işlemi değil, sadece kendi şubesinde onay bekleyen EFT'leri görmelidir:

### A. API Seviyesinde Havuz Filtreleme
```csharp
[HttpGet("pending-approvals")]
public async Task<ActionResult<List<WorkflowInstanceModel>>> GetPendingApprovals(
    [FromServices] WorkflowEngine engine,
    [FromServices] ICurrentBranchContext branchContext,
    CancellationToken ct)
{
    // Yöneticiler kendi şubelerine ait veya Genel Müdürlük tüm bekleyenleri listeler
    var instances = await engine.GetInstancesAsync(
        workflow: "eft-approval",
        state: "pending-branch-manager",
        status: WorkflowInstanceStatus.Active,
        ct: ct);

    if (!branchContext.IsHeadquarters)
    {
        instances = instances.Where(i => i.Data["branchId"]?.GetValue<int>() == branchContext.BranchId).ToList();
    }

    return instances;
}
```

---

## 8. Best Practice Kontrol Listesi (Checklist)

Yeni bir ekran veya iş akışı geliştirirken bu adımları mutlaka doğrulayın:

- [ ] **Şube Bilgisi Token'da Var mı?** Kullanıcının `branch_id` bilgisi login anında JWT/Identity claims içerisine yazıldı mı?
- [ ] **EF Core Seviyesinde Global Filtre Var mı?** Yeni eklenen entity `HasQueryFilter` ile şubeye bağlandı mı?
- [ ] **Controller'da `ApplyAccessFilter` Override Edildi mi?** Liste endpoint'leri kullanıcı şubesi ile filtreleniyor mu?
- [ ] **IDOR Koruması Sağlandı mı?** `AuthorizeAsync` yetkisiz isteklerde `404 Not Found` dönüyor mu?
- [ ] **İstemci Sahte Şube Kodu Gönderebilir mi?** Şube kolonları `[Editable(false)]` ile korunup, insert sırasında oturumdaki şubeyle eziliyor mu?
- [ ] **İş Akışında Şube Kuralı Var mı?** Şube onay geçişlerinde `user.branchId == data.branchId` kontrolü yazıldı mı?
- [ ] **Lookuplar İzolasyona Uygun mu?** Personel/hesap seçim kutuları sadece ilgili şubenin kayıtlarını çekiyor mu?
