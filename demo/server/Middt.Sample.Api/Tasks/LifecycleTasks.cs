using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Api.Cache;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;
using Middt.Sample.Api.Controllers;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Tasks;

/// <summary>
/// Entities with a lifecycle workflow (<c>entity</c> config value of <c>bike-load-record</c> / <c>bike-set-active</c>):
/// store, staff, category, product, customer.
/// </summary>
internal static class LifecycleEntities
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal) { "store", "staff", "category", "product", "customer" };

    public static IEnumerable<string> ValidateEntity(TaskDefinition task) =>
        task.Config["entity"] is JsonValue value && value.TryGetValue<string>(out var entity) && Names.Contains(entity)
            ? []
            : [$"'entity' must be one of: {string.Join(", ", Names)}."];

    /// <summary>Drops the cached lookup lists (store and staff <c>getall</c> are cached).</summary>
    public static async Task InvalidateCacheAsync(WorkflowTaskContext context, string entity, CancellationToken cancellationToken)
    {
        var key = entity switch
        {
            "store" => StoreController.AllStoresCacheKey,
            "staff" => StaffController.AllStaffCacheKey,
            _ => null,
        };

        if (key is not null && context.Services.GetService<IBaseCache>() is { } cache)
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
    }
}

/// <summary>
/// <c>bike-load-record</c>: reads the current values of a lifecycle record into a new object at <c>into</c> (no write):
/// the column values in camelCase (nulls left out) plus <c>active</c> (bool). Lifecycle workflows load the record after
/// creating it, after every approved change and when they adopt an existing record.
/// <code>{ "entity": "store", "id": "{{ data.storeId }}", "into": "store" }</code>
/// </summary>
public sealed class LoadRecordTask : BikeTask
{
    /// <summary>camelCase like the API, nulls left out.</summary>
    private static readonly JsonSerializerOptions RecordJson = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    protected override IReadOnlyList<string> RequiredArguments { get; } = ["entity", "id", "into"];

    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions) =>
        base.Validate(task, expressions).Concat(LifecycleEntities.ValidateEntity(task));

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var id = args.RequiredInt("id");
        var values = args.RequiredText("entity") switch
        {
            "store" => await db.Stores.AsNoTracking().Where(s => s.StoreId == id).Select(s => new
            {
                s.StoreId,
                s.StoreName,
                s.Phone,
                s.Email,
                s.Street,
                s.City,
                s.State,
                s.ZipCode,
                s.Active,
            }).FirstOrDefaultAsync(cancellationToken) as object,
            "staff" => await db.Staffs.AsNoTracking().Where(s => s.StaffId == id).Select(s => new
            {
                s.StaffId,
                s.FirstName,
                s.LastName,
                s.Email,
                s.Phone,
                s.StoreId,
                StoreName = s.Store.StoreName,
                s.ManagerId,
                Active = s.Active == 1,
            }).FirstOrDefaultAsync(cancellationToken) as object,
            "category" => await db.Categories.AsNoTracking().Where(c => c.CategoryId == id).Select(c => new
            {
                c.CategoryId,
                c.CategoryName,
                c.Active,
            }).FirstOrDefaultAsync(cancellationToken) as object,
            "product" => await db.Products.AsNoTracking().Where(p => p.ProductId == id).Select(p => new
            {
                p.ProductId,
                p.ProductName,
                p.BrandId,
                BrandName = p.Brand.BrandName,
                p.CategoryId,
                CategoryName = p.Category.CategoryName,
                p.ModelYear,
                p.ListPrice,
                p.Active,
            }).FirstOrDefaultAsync(cancellationToken) as object,
            "customer" => await db.Customers.AsNoTracking().Where(c => c.CustomerId == id).Select(c => new
            {
                c.CustomerId,
                c.FirstName,
                c.LastName,
                c.Email,
                c.Phone,
                c.Street,
                c.City,
                c.State,
                c.ZipCode,
                c.Active,
            }).FirstOrDefaultAsync(cancellationToken) as object,
            var entity => throw new WorkflowTaskException($"'entity' {entity} is not a lifecycle entity."),
        } ?? throw new BikeRuleException("Kayıt bulunamadı.");

        var record = JsonSerializer.SerializeToNode(values, values.GetType(), RecordJson);
        SetData(context, args.Into("record"), record);
    }
}

/// <summary>
/// <c>bike-set-active</c>: activates or deactivates (closes) a lifecycle record – soft delete, orders keep their
/// references. Naturally idempotent. A category with active products cannot be closed.
/// <code>{ "entity": "store", "id": "{{ data.storeId }}", "active": false }</code>
/// </summary>
public sealed class SetActiveTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["entity", "id", "active"];

    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions) =>
        base.Validate(task, expressions).Concat(LifecycleEntities.ValidateEntity(task));

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var entity = args.RequiredText("entity");
        var id = args.RequiredInt("id");
        var active = args.Bool("active");
        var updated = entity switch
        {
            "store" => await db.Stores.Where(s => s.StoreId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, active), cancellationToken),
            "staff" => await db.Staffs.Where(s => s.StaffId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, (byte)(active ? 1 : 0)), cancellationToken),
            "category" => await SetCategoryAsync(db, id, active, cancellationToken),
            "product" => await db.Products.Where(p => p.ProductId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, active), cancellationToken),
            "customer" => await db.Customers.Where(c => c.CustomerId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, active), cancellationToken),
            _ => throw new WorkflowTaskException($"'entity' {entity} is not a lifecycle entity."),
        };

        if (updated == 0)
        {
            throw new BikeRuleException("Kayıt bulunamadı.");
        }

        await LifecycleEntities.InvalidateCacheAsync(context, entity, cancellationToken);
    }

    private static async Task<int> SetCategoryAsync(BikeStoresContext db, int id, bool active, CancellationToken cancellationToken)
    {
        if (!active && await db.Products.AnyAsync(p => p.CategoryId == id && p.Active, cancellationToken))
        {
            throw new BikeRuleException("Bu kategoride aktif ürünler var: önce ürünleri kapatın.");
        }

        return await db.Categories.Where(c => c.CategoryId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, active), cancellationToken);
    }
}

/// <summary>Base of the <c>bike-update-*</c> tasks: loads the record (rule violation when it is gone) and saves the new values.</summary>
public abstract class UpdateRecordTask<TRecord> : BikeTask
    where TRecord : class
{
    protected abstract string Entity { get; }

    protected abstract Task<TRecord?> FindAsync(BikeStoresContext db, int id, CancellationToken cancellationToken);

    /// <summary>Checks the rules and copies the new values (setting the same values again is harmless: idempotent).</summary>
    protected abstract Task ApplyAsync(BikeStoresContext db, TRecord record, BikeTaskArguments args, CancellationToken cancellationToken);

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var record = await FindAsync(db, args.RequiredInt("id"), cancellationToken) ?? throw new BikeRuleException("Kayıt bulunamadı.");
        await ApplyAsync(db, record, args, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await LifecycleEntities.InvalidateCacheAsync(context, Entity, cancellationToken);
    }
}

/// <summary>
/// <c>bike-update-store</c>: changes a store (the name stays unique).
/// <code>{ "id": "{{ data.storeId }}", "storeName": "{{ data.change.storeName }}", "phone": …, "email": …, "street": …, "city": …, "state": …, "zipCode": … }</code>
/// </summary>
public sealed class UpdateStoreTask : UpdateRecordTask<Store>
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["id", "storeName"];

    protected override string Entity => "store";

    protected override Task<Store?> FindAsync(BikeStoresContext db, int id, CancellationToken cancellationToken) =>
        db.Stores.FirstOrDefaultAsync(s => s.StoreId == id, cancellationToken);

    protected override async Task ApplyAsync(BikeStoresContext db, Store store, BikeTaskArguments args, CancellationToken cancellationToken)
    {
        var name = args.RequiredText("storeName");
        if (await db.Stores.AnyAsync(s => s.StoreName == name && s.StoreId != store.StoreId, cancellationToken))
        {
            throw new BikeRuleException($"\"{name}\" adında bir mağaza zaten var.");
        }

        store.StoreName = name;
        store.Phone = args.Text("phone");
        store.Email = args.Text("email");
        store.Street = args.Text("street");
        store.City = args.Text("city");
        store.State = args.Text("state");
        store.ZipCode = args.Text("zipCode");
    }
}

/// <summary>
/// <c>bike-update-staff</c>: changes a staff member (unique e-mail, active store and manager).
/// <code>{ "id": "{{ data.staffId }}", "storeId": …, "firstName": …, "lastName": …, "email": …, "phone": …, "managerId": … }</code>
/// </summary>
public sealed class UpdateStaffTask : UpdateRecordTask<Staff>
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["id", "storeId", "firstName", "lastName", "email"];

    protected override string Entity => "staff";

    protected override Task<Staff?> FindAsync(BikeStoresContext db, int id, CancellationToken cancellationToken) =>
        db.Staffs.FirstOrDefaultAsync(s => s.StaffId == id, cancellationToken);

    protected override async Task ApplyAsync(BikeStoresContext db, Staff staff, BikeTaskArguments args, CancellationToken cancellationToken)
    {
        var storeId = args.RequiredInt("storeId");
        var managerId = args.WholeNumber("managerId");
        var email = args.RequiredText("email");
        await ActiveRecords.EnsureStoreAsync(db, storeId, cancellationToken);
        if (managerId == staff.StaffId)
        {
            throw new BikeRuleException("Personel kendi yöneticisi olamaz.");
        }

        if (managerId is { } manager)
        {
            await ActiveRecords.EnsureManagerAsync(db, manager, cancellationToken);
        }

        if (await db.Staffs.AnyAsync(s => s.Email == email && s.StaffId != staff.StaffId, cancellationToken))
        {
            throw new BikeRuleException($"{email} e-posta adresiyle kayıtlı bir personel var.");
        }

        staff.StoreId = storeId;
        staff.ManagerId = managerId;
        staff.FirstName = args.RequiredText("firstName");
        staff.LastName = args.RequiredText("lastName");
        staff.Email = email;
        staff.Phone = args.Text("phone");
    }
}

/// <summary>
/// <c>bike-update-category</c>: renames a category (the name stays unique).
/// <code>{ "id": "{{ data.categoryId }}", "categoryName": "{{ data.change.categoryName }}" }</code>
/// </summary>
public sealed class UpdateCategoryTask : UpdateRecordTask<Category>
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["id", "categoryName"];

    protected override string Entity => "category";

    protected override Task<Category?> FindAsync(BikeStoresContext db, int id, CancellationToken cancellationToken) =>
        db.Categories.FirstOrDefaultAsync(c => c.CategoryId == id, cancellationToken);

    protected override async Task ApplyAsync(BikeStoresContext db, Category category, BikeTaskArguments args, CancellationToken cancellationToken)
    {
        var name = args.RequiredText("categoryName");
        if (await db.Categories.AnyAsync(c => c.CategoryName == name && c.CategoryId != category.CategoryId, cancellationToken))
        {
            throw new BikeRuleException($"\"{name}\" adında bir kategori zaten var.");
        }

        category.CategoryName = name;
    }
}

/// <summary>
/// <c>bike-update-product</c>: changes a product (unique name + model year, existing brand, active category).
/// <code>{ "id": "{{ data.productId }}", "productName": …, "brandId": …, "categoryId": …, "modelYear": …, "listPrice": … }</code>
/// </summary>
public sealed class UpdateProductTask : UpdateRecordTask<Product>
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["id", "productName", "brandId", "categoryId", "modelYear", "listPrice"];

    protected override string Entity => "product";

    protected override Task<Product?> FindAsync(BikeStoresContext db, int id, CancellationToken cancellationToken) =>
        db.Products.FirstOrDefaultAsync(p => p.ProductId == id, cancellationToken);

    protected override async Task ApplyAsync(BikeStoresContext db, Product product, BikeTaskArguments args, CancellationToken cancellationToken)
    {
        var name = args.RequiredText("productName");
        var brandId = args.RequiredInt("brandId");
        var categoryId = args.RequiredInt("categoryId");
        var modelYear = args.RequiredInt("modelYear");
        if (!await db.Brands.AnyAsync(b => b.BrandId == brandId, cancellationToken))
        {
            throw new BikeRuleException("Seçilen marka bulunamadı.");
        }

        // The current (possibly closed) category may stay; a different one must be active.
        if (categoryId != product.CategoryId)
        {
            await ActiveRecords.EnsureCategoryAsync(db, categoryId, cancellationToken);
        }

        if (modelYear is < 1900 or > 2100)
        {
            throw new BikeRuleException("Model yılı 1900 ile 2100 arasında olmalıdır.");
        }

        if (await db.Products.AnyAsync(p => p.ProductName == name && p.ModelYear == modelYear && p.ProductId != product.ProductId, cancellationToken))
        {
            throw new BikeRuleException($"\"{name}\" ({modelYear}) ürünü zaten var.");
        }

        product.ProductName = name;
        product.BrandId = brandId;
        product.CategoryId = categoryId;
        product.ModelYear = (short)modelYear;
        product.ListPrice = args.RequiredNumber("listPrice");
    }
}

/// <summary>
/// <c>bike-update-customer</c>: changes a customer (the e-mail stays unique).
/// <code>{ "id": "{{ data.customerId }}", "firstName": …, "lastName": …, "email": …, "phone": …, "street": …, "city": …, "state": …, "zipCode": … }</code>
/// </summary>
public sealed class UpdateCustomerTask : UpdateRecordTask<Customer>
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["id", "firstName", "lastName", "email"];

    protected override string Entity => "customer";

    protected override Task<Customer?> FindAsync(BikeStoresContext db, int id, CancellationToken cancellationToken) =>
        db.Customers.FirstOrDefaultAsync(c => c.CustomerId == id, cancellationToken);

    protected override async Task ApplyAsync(BikeStoresContext db, Customer customer, BikeTaskArguments args, CancellationToken cancellationToken)
    {
        var email = args.RequiredText("email");
        if (await db.Customers.AnyAsync(c => c.Email == email && c.CustomerId != customer.CustomerId, cancellationToken))
        {
            throw new BikeRuleException($"{email} e-posta adresiyle kayıtlı bir müşteri var.");
        }

        customer.FirstName = args.RequiredText("firstName");
        customer.LastName = args.RequiredText("lastName");
        customer.Email = email;
        customer.Phone = args.Text("phone");
        customer.Street = args.Text("street");
        customer.City = args.Text("city");
        customer.State = args.Text("state");
        customer.ZipCode = args.Text("zipCode");
    }
}

/// <summary>Reference checks shared by the bike tasks: a closed (passive) record cannot be chosen for new data.</summary>
internal static class ActiveRecords
{
    public static async Task EnsureStoreAsync(BikeStoresContext db, int storeId, CancellationToken cancellationToken)
    {
        var store = await db.Stores.AsNoTracking().Where(s => s.StoreId == storeId).Select(s => new { s.StoreName, s.Active }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException("Seçilen mağaza bulunamadı.");
        if (!store.Active)
        {
            throw new BikeRuleException($"{store.StoreName} mağazası pasif durumda.");
        }
    }

    public static async Task EnsureManagerAsync(BikeStoresContext db, int staffId, CancellationToken cancellationToken)
    {
        var manager = await db.Staffs.AsNoTracking().Where(s => s.StaffId == staffId).Select(s => new { s.FirstName, s.LastName, s.Active }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException("Seçilen yönetici bulunamadı.");
        if (manager.Active != 1)
        {
            throw new BikeRuleException($"Yönetici {manager.FirstName} {manager.LastName} pasif durumda.");
        }
    }

    public static async Task EnsureCategoryAsync(BikeStoresContext db, int categoryId, CancellationToken cancellationToken)
    {
        var category = await db.Categories.AsNoTracking().Where(c => c.CategoryId == categoryId).Select(c => new { c.CategoryName, c.Active }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException("Seçilen kategori bulunamadı.");
        if (!category.Active)
        {
            throw new BikeRuleException($"{category.CategoryName} kategorisi pasif durumda.");
        }
    }
}
