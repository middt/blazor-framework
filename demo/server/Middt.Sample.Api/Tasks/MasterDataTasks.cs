using Microsoft.EntityFrameworkCore;
using Middt.Framework.Workflow.Tasks;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Common.Messages;
using Rebus.Bus;

namespace Middt.Sample.Api.Tasks;

/// <summary>
/// <c>bike-create-store</c>: inserts a store.
/// <code>{ "storeName": "{{ data.store.storeName }}", "phone": …, "email": …, "street": …, "city": …, "state": …, "zipCode": …, "into": "storeId" }</code>
/// </summary>
public sealed class CreateStoreTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["storeName"];

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var into = args.Into("storeId");
        if (StoredId(context, into) is { } existing && await db.Stores.AnyAsync(s => s.StoreId == existing, cancellationToken))
        {
            return;
        }

        var name = args.RequiredText("storeName");
        if (await db.Stores.AnyAsync(s => s.StoreName == name, cancellationToken))
        {
            throw new BikeRuleException($"\"{name}\" adında bir mağaza zaten var.");
        }

        var store = new Store
        {
            StoreName = name,
            Phone = args.Text("phone"),
            Email = args.Text("email"),
            Street = args.Text("street"),
            City = args.Text("city"),
            State = args.Text("state"),
            ZipCode = args.Text("zipCode"),
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync(cancellationToken);
        SetData(context, into, store.StoreId);
        await LifecycleEntities.InvalidateCacheAsync(context, "store", cancellationToken); // the cached store list
    }
}

/// <summary>
/// <c>bike-create-staff</c>: inserts an active staff member of an active store (the manager must be active too).
/// <code>{ "storeId": …, "firstName": …, "lastName": …, "email": …, "phone": …, "managerId": …, "into": "staffId" }</code>
/// </summary>
public sealed class CreateStaffTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["storeId", "firstName", "lastName", "email"];

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var into = args.Into("staffId");
        if (StoredId(context, into) is { } existing && await db.Staffs.AnyAsync(s => s.StaffId == existing, cancellationToken))
        {
            return;
        }

        var storeId = args.RequiredInt("storeId");
        var managerId = args.WholeNumber("managerId");
        var email = args.RequiredText("email");
        await ActiveRecords.EnsureStoreAsync(db, storeId, cancellationToken);
        if (managerId is { } manager)
        {
            await ActiveRecords.EnsureManagerAsync(db, manager, cancellationToken);
        }

        if (await db.Staffs.AnyAsync(s => s.Email == email, cancellationToken))
        {
            throw new BikeRuleException($"{email} e-posta adresiyle kayıtlı bir personel var.");
        }

        var staff = new Staff
        {
            StoreId = storeId,
            ManagerId = managerId,
            FirstName = args.RequiredText("firstName"),
            LastName = args.RequiredText("lastName"),
            Email = email,
            Phone = args.Text("phone"),
            Active = 1,
        };
        db.Staffs.Add(staff);
        await db.SaveChangesAsync(cancellationToken);
        SetData(context, into, staff.StaffId);
        await LifecycleEntities.InvalidateCacheAsync(context, "staff", cancellationToken); // the cached staff list
    }
}

/// <summary>
/// <c>bike-create-category</c>: inserts a category and publishes <see cref="CategoryCreated"/> (like the Category API).
/// <code>{ "categoryName": "{{ data.categoryName }}", "into": "categoryId" }</code>
/// </summary>
public sealed partial class CreateCategoryTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["categoryName"];

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var into = args.Into("categoryId");
        if (StoredId(context, into) is { } existing && await db.Categories.AnyAsync(c => c.CategoryId == existing, cancellationToken))
        {
            return;
        }

        var name = args.RequiredText("categoryName");
        if (await db.Categories.AnyAsync(c => c.CategoryName == name, cancellationToken))
        {
            throw new BikeRuleException($"\"{name}\" adında bir kategori zaten var.");
        }

        var category = new Category { CategoryName = name };
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        SetData(context, into, category.CategoryId);

        if (context.Services.GetService<IBus>() is { } bus)
        {
            try
            {
                var time = context.Services.GetService<TimeProvider>() ?? TimeProvider.System;
                await bus.Publish(new CategoryCreated(category.CategoryId, category.CategoryName, time.GetUtcNow()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A notification: the category exists, failing (and retrying) the task would not undo the insert.
                LogPublishFailed(context.Logger, ex, category.CategoryId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "CategoryCreated could not be published for category {CategoryId}")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, int categoryId);
}

/// <summary>
/// <c>bike-create-product</c>: inserts a product of an active category.
/// <code>{ "productName": …, "brandId": …, "categoryId": …, "modelYear": …, "listPrice": …, "into": "productId" }</code>
/// </summary>
public sealed class CreateProductTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["productName", "brandId", "categoryId", "modelYear", "listPrice"];

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var into = args.Into("productId");
        if (StoredId(context, into) is { } existing && await db.Products.AnyAsync(p => p.ProductId == existing, cancellationToken))
        {
            return;
        }

        var name = args.RequiredText("productName");
        var brandId = args.RequiredInt("brandId");
        var categoryId = args.RequiredInt("categoryId");
        var modelYear = args.RequiredInt("modelYear");
        if (!await db.Brands.AnyAsync(b => b.BrandId == brandId, cancellationToken))
        {
            throw new BikeRuleException("Seçilen marka bulunamadı.");
        }

        await ActiveRecords.EnsureCategoryAsync(db, categoryId, cancellationToken);

        if (modelYear is < 1900 or > 2100)
        {
            throw new BikeRuleException("Model yılı 1900 ile 2100 arasında olmalıdır.");
        }

        if (await db.Products.AnyAsync(p => p.ProductName == name && p.ModelYear == modelYear, cancellationToken))
        {
            throw new BikeRuleException($"\"{name}\" ({modelYear}) ürünü zaten var.");
        }

        var product = new Product
        {
            ProductName = name,
            BrandId = brandId,
            CategoryId = categoryId,
            ModelYear = (short)modelYear,
            ListPrice = args.RequiredNumber("listPrice"),
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        SetData(context, into, product.ProductId);
    }
}

/// <summary>
/// <c>bike-create-customer</c>: inserts a customer (the e-mail must be unused).
/// <code>{ "firstName": …, "lastName": …, "email": …, "phone": …, "street": …, "city": …, "state": …, "zipCode": …, "into": "customerId" }</code>
/// </summary>
public sealed class CreateCustomerTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["firstName", "lastName", "email"];

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var into = args.Into("customerId");
        if (StoredId(context, into) is { } existing && await db.Customers.AnyAsync(c => c.CustomerId == existing, cancellationToken))
        {
            return;
        }

        var email = args.RequiredText("email");
        if (await db.Customers.AnyAsync(c => c.Email == email, cancellationToken))
        {
            throw new BikeRuleException($"{email} e-posta adresiyle kayıtlı bir müşteri var.");
        }

        var customer = new Customer
        {
            FirstName = args.RequiredText("firstName"),
            LastName = args.RequiredText("lastName"),
            Email = email,
            Phone = args.Text("phone"),
            Street = args.Text("street"),
            City = args.Text("city"),
            State = args.Text("state"),
            ZipCode = args.Text("zipCode"),
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        SetData(context, into, customer.CustomerId);
    }
}
