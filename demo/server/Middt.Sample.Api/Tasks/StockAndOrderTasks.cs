using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Tasks;

/// <summary>
/// <c>bike-adjust-stock</c>: adds <c>quantity</c> (negative = takes) to the stock of a product in a store, never below 0.
/// <c>operation</c> is the data path of the idempotency marker (e.g. <c>reservedStock</c>): the result
/// <c>{ storeId, productId, delta, quantity }</c> is stored there and a run with the marker present does nothing.
/// <code>{ "storeId": "{{ data.storeId }}", "productId": "{{ data.productId }}", "quantity": "{{ -data.quantity }}", "operation": "reservedStock" }</code>
/// </summary>
public sealed class AdjustStockTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["storeId", "productId", "quantity", "operation"];

    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions) =>
        base.Validate(task, expressions).Concat(task.Config["operation"] is { } operation && !IsDataPath(operation)
            ? ["'operation' must be a data path (letters, digits, dots)."]
            : []);

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var marker = args.RequiredText("operation");
        if (JsonPath.Get(context.Data, marker) is not null)
        {
            return; // already applied
        }

        var storeId = args.RequiredInt("storeId");
        var productId = args.RequiredInt("productId");
        var delta = args.RequiredInt("quantity");
        var (storeName, productName) = await StockNames.LoadAsync(db, storeId, productId, cancellationToken);

        // One conditional UPDATE: concurrent reservations cannot take the stock below zero.
        var stock = db.Stocks.Where(s => s.StoreId == storeId && s.ProductId == productId);
        var updated = await stock.Where(s => (s.Quantity ?? 0) + delta >= 0)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Quantity, s => (s.Quantity ?? 0) + delta), cancellationToken);
        if (updated == 0)
        {
            var current = await stock.Select(s => (int?)(s.Quantity ?? 0)).FirstOrDefaultAsync(cancellationToken);
            if (current is not null || delta < 0)
            {
                throw new BikeRuleException(string.Create(CultureInfo.InvariantCulture,
                    $"Stok yetersiz: {storeName} mağazasında \"{productName}\" için {current ?? 0} adet var, {-delta} adet gerekiyor."));
            }

            db.Stocks.Add(new Stock { StoreId = storeId, ProductId = productId, Quantity = delta });
            await db.SaveChangesAsync(cancellationToken);
        }

        var quantity = await stock.Select(s => s.Quantity ?? 0).FirstAsync(cancellationToken);
        SetData(context, marker, new JsonObject
        {
            ["storeId"] = storeId,
            ["productId"] = productId,
            ["delta"] = delta,
            ["quantity"] = quantity,
        });
    }
}

/// <summary>
/// <c>bike-check-stock</c>: reads the stock (no write) into <c>{ available, required, missing, enough }</c>.
/// <code>{ "storeId": …, "productId": …, "quantity": …, "into": "stockCheck" }</code>
/// </summary>
public sealed class CheckStockTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["storeId", "productId", "quantity"];

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var storeId = args.RequiredInt("storeId");
        var productId = args.RequiredInt("productId");
        var required = args.RequiredInt("quantity");
        await StockNames.LoadAsync(db, storeId, productId, cancellationToken);

        var available = await db.Stocks.Where(s => s.StoreId == storeId && s.ProductId == productId)
            .Select(s => s.Quantity ?? 0).FirstOrDefaultAsync(cancellationToken);
        SetData(context, args.Into("stockCheck"), new JsonObject
        {
            ["available"] = available,
            ["required"] = required,
            ["missing"] = Math.Max(0, required - available),
            ["enough"] = available >= required,
        });
    }
}

/// <summary>
/// <c>bike-load-order-info</c>: validates the order lines (active customer, product and store) and fills the display/pricing keys the order views read:
/// <c>customerName</c>, <c>email</c>, <c>storeName</c>, <c>product</c> (name), <c>listPrice</c> and
/// <c>amount</c> = list price × quantity (no write).
/// <code>{ "customerId": "{{ data.customerId }}", "storeId": …, "productId": …, "quantity": … }</code>
/// </summary>
public sealed class LoadOrderInfoTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["customerId", "storeId", "productId", "quantity"];

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var customerId = args.RequiredInt("customerId");
        var storeId = args.RequiredInt("storeId");
        var productId = args.RequiredInt("productId");
        var quantity = args.RequiredInt("quantity");
        if (quantity < 1)
        {
            throw new BikeRuleException("Adet en az 1 olmalıdır.");
        }

        var customer = await db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId)
            .Select(c => new { c.FirstName, c.LastName, c.Email, c.Active }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException("Seçilen müşteri bulunamadı.");
        var product = await db.Products.AsNoTracking().Where(p => p.ProductId == productId)
            .Select(p => new { p.ProductName, p.ListPrice, p.Active }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException("Seçilen ürün bulunamadı.");
        var store = await db.Stores.AsNoTracking().Where(s => s.StoreId == storeId).Select(s => new { s.StoreName, s.Active }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException("Seçilen mağaza bulunamadı.");
        if (!customer.Active)
        {
            throw new BikeRuleException($"Müşteri {customer.FirstName} {customer.LastName} pasif durumda.");
        }

        if (!product.Active)
        {
            throw new BikeRuleException($"\"{product.ProductName}\" ürünü pasif durumda.");
        }

        if (!store.Active)
        {
            throw new BikeRuleException($"{store.StoreName} mağazası pasif durumda.");
        }

        var storeName = store.StoreName;

        context.Data["customerName"] = $"{customer.FirstName} {customer.LastName}".Trim();
        context.Data["email"] = customer.Email;
        context.Data["storeName"] = storeName;
        context.Data["product"] = product.ProductName;
        context.Data["listPrice"] = product.ListPrice;
        context.Data["amount"] = product.ListPrice * quantity;
    }
}

/// <summary>
/// <c>bike-create-order</c>: inserts an order (status 1 = Pending) with one order item for an active customer, store and product. The order is taken by the store's
/// manager (or another active staff member of the store).
/// <code>{ "customerId": …, "storeId": …, "productId": …, "quantity": …, "listPrice": …, "discount": 0, "requiredInDays": 7, "into": "orderId" }</code>
/// </summary>
public sealed class CreateOrderTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["customerId", "storeId", "productId", "quantity"];

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var into = args.Into("orderId");
        if (StoredId(context, into) is { } existing && await db.Orders.AnyAsync(o => o.OrderId == existing, cancellationToken))
        {
            return;
        }

        var customerId = args.RequiredInt("customerId");
        var storeId = args.RequiredInt("storeId");
        var productId = args.RequiredInt("productId");
        var quantity = args.RequiredInt("quantity");
        var customer = await db.Customers.AsNoTracking().Where(c => c.CustomerId == customerId)
            .Select(c => new { c.FirstName, c.LastName, c.Active }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException("Seçilen müşteri bulunamadı.");
        if (!customer.Active)
        {
            throw new BikeRuleException($"Müşteri {customer.FirstName} {customer.LastName} pasif durumda.");
        }

        var (storeName, _) = await StockNames.LoadAsync(db, storeId, productId, cancellationToken, requireActive: true);
        var listPrice = args.Number("listPrice")
            ?? await db.Products.Where(p => p.ProductId == productId).Select(p => p.ListPrice).FirstAsync(cancellationToken);
        var staffId = await db.Staffs.Where(s => s.StoreId == storeId && s.Active == 1)
            .OrderBy(s => s.ManagerId == null ? 0 : 1).ThenBy(s => s.StaffId)
            .Select(s => (int?)s.StaffId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException($"{storeName} mağazasında aktif personel yok: önce personel alımı akışıyla personel ekleyin.");

        var today = Today(context);
        var order = new Order
        {
            CustomerId = customerId,
            StoreId = storeId,
            StaffId = staffId,
            OrderStatus = 1,
            OrderDate = today,
            RequiredDate = today.AddDays(args.WholeNumber("requiredInDays") ?? 7),
        };
        order.OrderItems.Add(new OrderItem
        {
            ItemId = 1,
            ProductId = productId,
            Quantity = quantity,
            ListPrice = listPrice,
            Discount = args.Number("discount") ?? 0m,
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken); // order + item in one statement batch (one transaction)
        SetData(context, into, order.OrderId);
        SetData(context, "staffId", staffId);
    }
}

/// <summary>
/// <c>bike-set-order-status</c>: sets <c>order_status</c> (1 Pending, 2 Processing, 3 Rejected, 4 Completed) and, with
/// <c>"shipped": true</c>, the shipped date (once). Naturally idempotent.
/// <code>{ "orderId": "{{ data.orderId }}", "status": 4, "shipped": true }</code>
/// </summary>
public sealed class SetOrderStatusTask : BikeTask
{
    protected override IReadOnlyList<string> RequiredArguments { get; } = ["orderId", "status"];

    public override IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions) =>
        base.Validate(task, expressions).Concat(task.Config["status"] is JsonValue status && status.TryGetValue<int>(out var value) && value is < 1 or > 4
            ? ["'status' must be between 1 and 4."]
            : []);

    protected override async Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken)
    {
        var orderId = args.RequiredInt("orderId");
        var status = args.RequiredInt("status");
        if (status is < 1 or > 4)
        {
            throw new WorkflowTaskException("'status' must be between 1 and 4.");
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken)
            ?? throw new BikeRuleException(string.Create(CultureInfo.InvariantCulture, $"Sipariş #{orderId} bulunamadı."));
        order.OrderStatus = (byte)status;
        if (args.Bool("shipped"))
        {
            order.ShippedDate ??= Today(context);
        }

        await db.SaveChangesAsync(cancellationToken);
        SetData(context, args.Into("orderStatus"), status);
    }
}

internal static class StockNames
{
    /// <summary>
    /// Store and product names (rule violation when one of them does not exist or, with <paramref name="requireActive"/>,
    /// is closed). Stock movements of existing orders (reservation, return) work for closed records too.
    /// </summary>
    public static async Task<(string Store, string Product)> LoadAsync(BikeStoresContext db, int storeId, int productId, CancellationToken cancellationToken, bool requireActive = false)
    {
        var store = await db.Stores.AsNoTracking().Where(s => s.StoreId == storeId).Select(s => new { s.StoreName, s.Active }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException("Seçilen mağaza bulunamadı.");
        var product = await db.Products.AsNoTracking().Where(p => p.ProductId == productId).Select(p => new { p.ProductName, p.Active }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BikeRuleException("Seçilen ürün bulunamadı.");
        if (requireActive && !store.Active)
        {
            throw new BikeRuleException($"{store.StoreName} mağazası pasif durumda.");
        }

        if (requireActive && !product.Active)
        {
            throw new BikeRuleException($"\"{product.ProductName}\" ürünü pasif durumda.");
        }

        return (store.StoreName, product.ProductName);
    }
}

/// <summary>Registers the bike store tasks (<c>bike-*</c>) used by the demo workflow definitions.</summary>
public static class BikeWorkflowTasks
{
    public static Middt.Framework.Workflow.MiddtWorkflowOptions AddBikeStoreTasks(this Middt.Framework.Workflow.MiddtWorkflowOptions options) => options
        .AddTask<CreateStoreTask>("bike-create-store")
        .AddTask<CreateStaffTask>("bike-create-staff")
        .AddTask<CreateCategoryTask>("bike-create-category")
        .AddTask<CreateProductTask>("bike-create-product")
        .AddTask<CreateCustomerTask>("bike-create-customer")
        .AddTask<LoadRecordTask>("bike-load-record")
        .AddTask<UpdateStoreTask>("bike-update-store")
        .AddTask<UpdateStaffTask>("bike-update-staff")
        .AddTask<UpdateCategoryTask>("bike-update-category")
        .AddTask<UpdateProductTask>("bike-update-product")
        .AddTask<UpdateCustomerTask>("bike-update-customer")
        .AddTask<SetActiveTask>("bike-set-active")
        .AddTask<AdjustStockTask>("bike-adjust-stock")
        .AddTask<CheckStockTask>("bike-check-stock")
        .AddTask<LoadOrderInfoTask>("bike-load-order-info")
        .AddTask<CreateOrderTask>("bike-create-order")
        .AddTask<SetOrderStatusTask>("bike-set-order-status");
}
