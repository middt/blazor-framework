# Middt Framework — Postman Koleksiyonları

Bu klasör, **Middt Framework** üzerindeki tüm iş akışlarını (`Workflows`) uçtan uca test edebilmeniz için hazırlanmış Postman v2.1 koleksiyonlarını ve ortam (`environment`) dosyasını içerir.

## Dosyalar

| Dosya | Açıklama |
|---|---|
| [`Middt.Local.postman_environment.json`](./Middt.Local.postman_environment.json) | Docker Compose yerel ortam değişkenleri (`apiUrl: http://localhost:8180`, `idpUrl: http://localhost:8083`, `clientId: middt-postman` ve otomatik doldurulan token/instance değişkenleri). |
| [`Middt-All-Workflows.postman_collection.json`](./Middt-All-Workflows.postman_collection.json) | **Tek pakette tüm akışlar**: Kimlik doğrulama + dinamik veri sorguları + 6 iş akışının tamamı. |
| [`01-Customer-Onboarding.postman_collection.json`](./01-Customer-Onboarding.postman_collection.json) | **Müşteri Kaydı & KYC (`customer-onboarding`)**: Otomatik onay, manuel KYC incelemesi, not ekleme, gelir güncelleme ve hesap kapatma. |
| [`02-Risk-Limit-Eval.postman_collection.json`](./02-Risk-Limit-Eval.postman_collection.json) | **Risk & Limit Değerlendirme (`risk-limit-eval`)**: Otomatik onay, manuel underwriting incelemesi ve otomatik red senaryoları. |
| [`03-Credit-Allocation.postman_collection.json`](./03-Credit-Allocation.postman_collection.json) | **Kredi Tahsis & 4-Göz Onay (`credit-allocation`)**: `loadWorkflow`, `SubFlow` (`risk-limit-eval` geçiş iletimi), teminat yeniden hesaplama (`$self` reenter) ve 2 farklı yönetici ile **4-Göz (`denyRoles: ["$previousUser"]`)** onayı. |
| [`04-Order-Payment-Orchestration.postman_collection.json`](./04-Order-Payment-Orchestration.postman_collection.json) | **Sipariş & Ödeme Orkestrasyonu (`order-payment-orchestration`)**: 3'lü paralel `fanOut`, `http` ödeme ağ geçidi çağrısı, `cache` (Redis) ve Rebus/RabbitMQ `publish` senaryoları. |
| [`05-SLA-Escalation.postman_collection.json`](./05-SLA-Escalation.postman_collection.json) | **SLA & Eskalasyon Yönetimi (`sla-escalation`)**: Zamanlayıcı (`Scheduled` transition), 5 saniyelik otomatik eskalasyon testi, zamanlayıcıyı sıfırlamadan `heartbeat-ping` (`reenter: false`) ve SLA uzatma (`reenter: true`). |
| [`06-Invoice-Collection.postman_collection.json`](./06-Invoice-Collection.postman_collection.json) | **Fatura Tahsilatı (`invoice-collection`)**: Tekil `record-payment` / `payment-received` ile kısmi veya tam ödeme kaydı (`into: "partialPayment[]"` dizisi, `delete-payment` ile hatalı kısmi ödeme kaydını silme ve bakiyeyi yeniden hesaplama, `input.amount <= data.remainingAmount` ile fazla ödeme engelleme -> 409, `paidAmount >= total` olduğunda `payment-completed` ile otomatik `reconciling` geçişi), JSON Patch ile güncelleme (`update-billing-contact`: `add`, `test` + `replace`, başarısız `test` → 409, `remove`), bekletmeli mesaj (`bufferFor`, 202), arka planda muhasebe kaydı (`background: true`) ve `history: delta`. |

## Hızlı Başlangıç

1. **İçe Aktarın (Import)**:
   - Postman'de **Import** butonuna tıklayın ve [`Middt.Local.postman_environment.json`](./Middt.Local.postman_environment.json) ile birlikte [`Middt-All-Workflows.postman_collection.json`](./Middt-All-Workflows.postman_collection.json) (veya istediğiniz tekil koleksiyonu) seçin.
   - *(Not: Koleksiyon içindeki scriptler değişkenleri hem `Environment` hem de `Collection Variables` içine otomatik kaydettiği için ortam seçmeyi unutsanız bile çalışır).*
2. **Token Alın**:
   - **`00 - Kimlik Doğrulama (Middt Identity OAuth2)`** klasöründeki:
     - `1. Token Al - Manager & Admin (test@test.com)` isteğini çalıştırın (`{{access_token}}` otomatik kaydedilir).
     - `credit-allocation` içindeki **4-Göz Onayı** adımını denemek için `2. Token Al - 2. Onaycı Manager (manager2@test.com - 4-Göz İçin)` isteğini de bir kez çalıştırın (`{{manager2_access_token}}` otomatik kaydedilir).
3. **Akışları Çalıştırın**:
   - Her akış klasöründeki **`2A` / `2B` Başlat (`POST .../instances`)** istekleri çalıştırıldığında dönen `instance.id` değeri ilgili `{{...InstanceId}}` değişkenine (ve müşteri anahtarı `{{customerKey}}` değişkenine) **otomatik olarak** yazılır.
   - Ardından aynı klasördeki `3. Akış Durumunu Sorgula` ve `4 / 5 / 6` geçiş (`transitions`) isteklerini hiçbir ID kopyalamadan sırayla çalıştırabilirsiniz.
