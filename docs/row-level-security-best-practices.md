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

### Yöntem 1: Akış Adımına Girerken SQL ile Yükleme (`db-query`)
Akışın ilgili adımına (`onEntry`) bir `db-query` görevi eklenir. Şube ID'si parametrik olarak verilerek liste akış verisine (`data.availableStaffList`) kaydedilir:

```json
{
  "key": "assign-staff-step",
  "title": "Müşteri Temsilcisi Atama",
  "onEntry": [
    {
      "task": "db-query",
      "config": {
        "sql": "SELECT staff_id AS id, first_name + ' ' + last_name AS name FROM staffs WHERE branch_id = {0} AND active = 1",
        "parameters": [ "{{ data.branchId }}" ],
        "into": "availableStaffList"
      }
    }
  ],
  "view": {
    "type": "Form",
    "schema": {
      "type": "object",
      "properties": {
        "selectedStaffId": {
          "type": "integer",
          "title": "Atanan Temsilci",
          "enumSource": "data.availableStaffList"
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

### Yöntem 2: Dinamik Lookup (`x-lookup` + Controller Satır Filtresi)
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
