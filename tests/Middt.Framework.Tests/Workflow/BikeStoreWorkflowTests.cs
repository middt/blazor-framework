using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Tests.Ui;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Tasks;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Tasks;

namespace Middt.Framework.Tests.Workflow;

/// <summary>
/// The linked demo workflows (demo/server/Middt.Sample.Api/Workflows) with the bike-* tasks writing a BikeStores
/// database (SQLite in-memory): store → staff, category, product → initial stock, customer, order → fulfillment.
/// </summary>
public sealed class BikeStoreWorkflowTests
{
    private static readonly WorkflowUser Alice = WorkflowTestHost.User("alice");
    private static readonly WorkflowUser Boss = WorkflowTestHost.User("boss", "manager");

    private sealed class Bike : IAsyncDisposable
    {
        private Bike(WorkflowTestHost host, SqliteConnection connection, Seed seed)
        {
            Host = host;
            Connection = connection;
            Ids = seed;
        }

        public WorkflowTestHost Host { get; }

        public SqliteConnection Connection { get; }

        public Seed Ids { get; }

        public static async Task<Bike> StartAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var definitions = Directory.GetFiles(UiSampleLayoutTests.SampleDirectory("Workflows"), "*.json").Select(File.ReadAllText);
            var host = await WorkflowTestHost.StartAsync(
                definitions,
                o =>
                {
                    o.UseApplicationDbContext<BikeStoresContext>();
                    o.AddBikeStoreTasks();
                },
                s =>
                {
                    s.AddDbContext<BikeStoresContext>(db => db.UseSqlite(connection));
                    s.AddSingleton<IWorkflowMessagePublisher, FakePublisher>();
                });

            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BikeStoresContext>();
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var brand = new Brand { BrandName = "Trek" };
            var category = new Category { CategoryName = "Road Bikes" };
            var store = new Store { StoreName = "Merkez", City = "Istanbul" };
            var emptyStore = new Store { StoreName = "Yeni", City = "Ankara" };
            db.AddRange(brand, category, store, emptyStore);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var staff = new Staff { FirstName = "Mia", LastName = "Manager", Email = "mia@bike.test", Active = 1, StoreId = store.StoreId };
            var product = new Product { ProductName = "Domane", BrandId = brand.BrandId, CategoryId = category.CategoryId, ModelYear = 2026, ListPrice = 1000m };
            var customer = new Customer { FirstName = "Ali", LastName = "Veli", Email = "ali@bike.test" };
            db.AddRange(staff, product, customer);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.Stocks.Add(new Stock { StoreId = store.StoreId, ProductId = product.ProductId, Quantity = 1 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            return new Bike(host, connection, new Seed(brand.BrandId, category.CategoryId, store.StoreId, emptyStore.StoreId, staff.StaffId, product.ProductId, customer.CustomerId));
        }

        public async Task<T> DbAsync<T>(Func<BikeStoresContext, Task<T>> query)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            return await query(scope.ServiceProvider.GetRequiredService<BikeStoresContext>());
        }

        public Task<int?> StockAsync(int storeId, int productId) =>
            DbAsync(db => db.Stocks.Where(s => s.StoreId == storeId && s.ProductId == productId).Select(s => s.Quantity).FirstOrDefaultAsync(TestContext.Current.CancellationToken));

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed record Seed(int BrandId, int CategoryId, int StoreId, int EmptyStoreId, int StaffId, int ProductId, int CustomerId);

    private static Guid Id(JsonObject data, string key) => Guid.Parse(data[key]!.GetValue<string>());

    [Fact]
    public async Task Store_opening_creates_the_store_and_starts_staff_onboarding_for_it()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var opening = await host.StartInstanceAsync("store-opening", user: Alice);
        await host.TransitionAsync(opening.Id, "submit", new { storeName = "Kadikoy", city = "Istanbul", zipCode = "34710" }, Alice);
        opening = await host.TransitionAsync(opening.Id, "approve", new { comment = "ok" }, Boss);

        opening.State.Should().Be("active", "the instance stays as the lifecycle of the store");
        opening.StateTitle.Should().Be("Aktif");
        opening.Status.Should().Be(WorkflowInstanceStatus.Active);
        var storeId = opening.Data["storeId"]!.GetValue<int>();
        opening.Key.Should().Be($"store-{storeId}");
        opening.Data["store"]!["storeName"]!.GetValue<string>().Should().Be("Kadikoy");
        opening.Data["store"]!["active"]!.GetValue<bool>().Should().BeTrue();
        opening.Data["change"]!["zipCode"]!.GetValue<string>().Should().Be("34710", "the change form is prefilled with the current values");
        opening.AvailableTransitions.Select(t => t.Key).Should().BeEquivalentTo("request-change", "request-close");
        opening.AvailableTransitions.Single(t => t.Key == "request-change").Should().Match<AvailableTransitionModel>(t => t.Inline && t.InputInto == "change");
        (await bike.DbAsync(db => db.Stores.SingleAsync(s => s.StoreId == storeId, TestContext.Current.CancellationToken))).ZipCode.Should().Be("34710");

        var onboarding = await host.GetAsync(Id(opening.Data, "staffOnboardingId"));
        onboarding.Workflow.Should().Be("staff-onboarding");
        onboarding.Key.Should().BeNull("the staff lifecycle gets its key once the staff member exists");
        onboarding.State.Should().Be("personal", "the store was chosen by store-opening");
        onboarding.AvailableTransitions.Select(t => t.Key).Should().NotContain("back");

        await host.TransitionAsync(onboarding.Id, "submit-personal", new { firstName = "Can", lastName = "Demir", email = "can@bike.test", managerId = bike.Ids.StaffId }, Boss);
        onboarding = await host.TransitionAsync(onboarding.Id, "approve", null, Boss);

        onboarding.State.Should().Be("active");
        var staffId = onboarding.Data["staffId"]!.GetValue<int>();
        onboarding.Key.Should().Be($"staff-{staffId}");
        onboarding.Data["staff"]!["storeName"]!.GetValue<string>().Should().Be("Kadikoy");
        var staff = await bike.DbAsync(db => db.Staffs.SingleAsync(s => s.StaffId == staffId, TestContext.Current.CancellationToken));
        staff.StoreId.Should().Be(storeId);
        staff.ManagerId.Should().Be(bike.Ids.StaffId);
        staff.Active.Should().Be(1);
    }

    [Fact]
    public async Task Rule_violation_shows_the_message_and_the_corrected_data_is_saved()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var onboarding = await host.StartInstanceAsync("staff-onboarding", user: Alice);
        onboarding.State.Should().Be("store");
        await host.TransitionAsync(onboarding.Id, "submit-store", new { storeId = bike.Ids.StoreId }, Alice);
        await host.TransitionAsync(onboarding.Id, "submit-personal", new { firstName = "Eda", lastName = "Kaya", email = "mia@bike.test" }, Alice);
        onboarding = await host.TransitionAsync(onboarding.Id, "approve", null, Boss);

        onboarding.State.Should().Be("fix");
        onboarding.Status.Should().Be(WorkflowInstanceStatus.Active, "a rule violation is not a fault");
        onboarding.Layout.Should().Be("bike-task-error");
        onboarding.Data["error"]!.GetValue<string>().Should().Be("mia@bike.test e-posta adresiyle kayıtlı bir personel var.");

        await host.TransitionAsync(onboarding.Id, "edit", null, Alice);
        await host.TransitionAsync(onboarding.Id, "submit-personal", new { firstName = "Eda", lastName = "Kaya", email = "eda@bike.test" }, Alice);
        onboarding = await host.TransitionAsync(onboarding.Id, "approve", null, Boss);

        onboarding.State.Should().Be("active");
        onboarding.Data["error"].Should().BeNull();
        (await bike.DbAsync(db => db.Staffs.CountAsync(s => s.Email == "eda@bike.test", TestContext.Current.CancellationToken))).Should().Be(1);
    }

    [Fact]
    public async Task Category_request_creates_the_category_only_when_approved()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var approved = await host.StartInstanceAsync("category-request", user: Alice);
        await host.TransitionAsync(approved.Id, "submit", new { categoryName = "Gravel", justification = "Yeni segment" }, Alice);
        approved = await host.TransitionAsync(approved.Id, "approve", null, Boss);

        var rejected = await host.StartInstanceAsync("category-request", user: Alice);
        await host.TransitionAsync(rejected.Id, "submit", new { categoryName = "Scooter", justification = "Deneme" }, Alice);
        rejected = await host.TransitionAsync(rejected.Id, "reject", new { reason = "Kapsam dışı" }, Boss);

        approved.State.Should().Be("active");
        approved.Data["categoryId"]!.GetValue<int>().Should().BePositive();
        approved.Data["justification"]!.GetValue<string>().Should().Be("Yeni segment");
        rejected.State.Should().Be("rejected");
        rejected.StateSubType.Should().Be("Error");
        (await bike.DbAsync(db => db.Categories.Select(c => c.CategoryName).ToListAsync(TestContext.Current.CancellationToken)))
            .Should().BeEquivalentTo("Road Bikes", "Gravel");
    }

    [Fact]
    public async Task Product_launch_needs_price_approval_and_starts_the_initial_stock_replenishment()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var launch = await host.StartInstanceAsync("product-launch", user: Alice);
        launch = await host.TransitionAsync(launch.Id, "submit", new
        {
            categoryId = bike.Ids.CategoryId, brandId = bike.Ids.BrandId, productName = "Madone", modelYear = 2027,
            listPrice = 12500.5m, initialStoreId = bike.Ids.EmptyStoreId, initialQuantity = 4,
        }, Alice);
        launch.State.Should().Be("price-approval", "list prices above 5.000 TL need a manager");

        launch = await host.TransitionAsync(launch.Id, "approve", null, Boss);

        launch.State.Should().Be("active");
        var productId = launch.Data["productId"]!.GetValue<int>();
        (await bike.DbAsync(db => db.Products.SingleAsync(p => p.ProductId == productId, TestContext.Current.CancellationToken))).ListPrice.Should().Be(12500.5m);

        var replenishment = await host.GetAsync(Id(launch.Data, "stockReplenishmentId"));
        replenishment.State.Should().Be("approval", "the initial stock is approved like any replenishment");
        (await bike.StockAsync(bike.Ids.EmptyStoreId, productId)).Should().BeNull();

        await host.RunJobsAsync(TimeSpan.FromMinutes(1));

        replenishment = await host.GetAsync(replenishment.Id);
        replenishment.State.Should().Be("completed");
        replenishment.Data["review"]!["approvedBy"]!.GetValue<string>().Should().Be("Otomatik onay (1 dakika)");
        (await bike.StockAsync(bike.Ids.EmptyStoreId, productId)).Should().Be(4);
    }

    [Fact]
    public async Task Cheap_product_is_approved_automatically_and_without_initial_stock_nothing_is_started()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var launch = await host.StartInstanceAsync("product-launch", user: Alice);
        launch = await host.TransitionAsync(launch.Id, "submit", new
        {
            categoryId = bike.Ids.CategoryId, brandId = bike.Ids.BrandId, productName = "Marlin", modelYear = 2026, listPrice = 900, initialQuantity = 0,
        }, Alice);

        launch.State.Should().Be("active");
        launch.Data["review"]!["approvedBy"]!.GetValue<string>().Should().StartWith("Otomatik");
        launch.Data.ContainsKey("stockReplenishmentId").Should().BeFalse();
    }

    [Fact]
    public async Task Corporate_customer_is_registered_with_company_name_and_contact_person()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var onboarding = await host.StartInstanceAsync("customer-onboarding", user: Alice);
        await host.TransitionAsync(onboarding.Id, "submit-type", new { customerType = "Kurumsal" }, Alice);
        await host.TransitionAsync(onboarding.Id, "submit-company", new { companyName = "Pedal AS", taxNumber = "1234567890", taxOffice = "Kadikoy", contactPerson = "Ayse Yilmaz" }, Alice);
        await host.TransitionAsync(onboarding.Id, "submit-contact", new { email = "info@pedal.test", city = "Istanbul", zipCode = "34000" }, Alice);
        onboarding = await host.TransitionAsync(onboarding.Id, "verify", null, Boss);

        onboarding.State.Should().Be("active");
        var customerId = onboarding.Data["customerId"]!.GetValue<int>();
        onboarding.Key.Should().Be($"customer-{customerId}");
        onboarding.Data["customer"]!["email"]!.GetValue<string>().Should().Be("info@pedal.test");
        var customer = await bike.DbAsync(db => db.Customers.SingleAsync(c => c.CustomerId == customerId, TestContext.Current.CancellationToken));
        customer.FirstName.Should().Be("Pedal AS");
        customer.LastName.Should().Be("Ayse Yilmaz");
        customer.Email.Should().Be("info@pedal.test");
    }

    [Fact]
    public async Task Approved_order_is_created_and_fulfilled_with_a_replenishment_subflow()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;
        var ids = bike.Ids;

        var order = await host.StartInstanceAsync("order-approval", user: Alice);
        order.WorkflowVersion.Should().Be(3);
        await host.TransitionAsync(order.Id, "submit-customer", new { customerId = ids.CustomerId, customerType = "Bireysel" }, Alice);
        order = await host.TransitionAsync(order.Id, "submit-order", new { storeId = ids.StoreId, productId = ids.ProductId, quantity = 3, deliveryType = "Standart", note = "Hediye" }, Alice);

        // amount 3 × 1000 ≤ 5000, standard delivery: approved automatically after the credit check.
        order.State.Should().Be("approved");
        order.Data["customerName"]!.GetValue<string>().Should().Be("Ali Veli");
        order.Data["email"]!.GetValue<string>().Should().Be("ali@bike.test");
        order.Data["product"]!.GetValue<string>().Should().Be("Domane");
        order.Data["amount"]!.GetValue<decimal>().Should().Be(3000m);
        order.Data["credit"]!["score"]!.GetValue<decimal>().Should().Be(85);
        var orderId = order.Data["orderId"]!.GetValue<int>();
        var row = await bike.DbAsync(db => db.Orders.Include(o => o.OrderItems).SingleAsync(o => o.OrderId == orderId, TestContext.Current.CancellationToken));
        row.CustomerId.Should().Be(ids.CustomerId);
        row.StaffId.Should().Be(ids.StaffId);
        row.OrderItems.Should().ContainSingle(i => i.ProductId == ids.ProductId && i.Quantity == 3 && i.ListPrice == 1000m);

        // Stock was 1: the fulfillment replenished the missing 2 (approved automatically), then reserved 3.
        var fulfillment = await host.GetAsync(Id(order.Data, "fulfillmentId"));
        fulfillment.Key.Should().Be($"order-{orderId}");
        fulfillment.State.Should().Be("picking");
        fulfillment.Data["stockCheck"]!["enough"]!.GetValue<bool>().Should().BeTrue();
        fulfillment.Data["replenishment"]!["quantity"]!.GetValue<int>().Should().Be(2);
        fulfillment.Data["reservedStock"]!["quantity"]!.GetValue<int>().Should().Be(0);
        (await bike.StockAsync(ids.StoreId, ids.ProductId)).Should().Be(0);
        (await bike.DbAsync(db => db.Orders.Where(o => o.OrderId == orderId).Select(o => o.OrderStatus).SingleAsync(TestContext.Current.CancellationToken))).Should().Be(2);

        await host.TransitionAsync(fulfillment.Id, "picked", null, Boss);
        fulfillment = await host.TransitionAsync(fulfillment.Id, "ship", new { carrier = "Aras Kargo", trackingNumber = "AR-123456" }, Boss);
        fulfillment.State.Should().Be("in-transit");
        (await bike.DbAsync(db => db.Orders.Where(o => o.OrderId == orderId).Select(o => o.ShippedDate).SingleAsync(TestContext.Current.CancellationToken))).Should().NotBeNull();

        await host.RunJobsAsync(TimeSpan.FromMinutes(3));

        fulfillment = await host.GetAsync(fulfillment.Id);
        fulfillment.State.Should().Be("delivered");
        fulfillment.Data["deliveredBy"]!.GetValue<string>().Should().StartWith("Otomatik");
        (await bike.DbAsync(db => db.Orders.Where(o => o.OrderId == orderId).Select(o => o.OrderStatus).SingleAsync(TestContext.Current.CancellationToken))).Should().Be(4);
    }

    [Fact]
    public async Task Rejected_order_creates_no_order()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var order = await host.StartInstanceAsync("order-approval", user: Alice);
        await host.TransitionAsync(order.Id, "submit-customer", new { customerId = bike.Ids.CustomerId, customerType = "Bireysel" }, Alice);
        order = await host.TransitionAsync(order.Id, "submit-order", new { storeId = bike.Ids.StoreId, productId = bike.Ids.ProductId, quantity = 6, deliveryType = "Standart" }, Alice);
        order.State.Should().Be("manager-review", "6 × 1000 > 5000");
        order.View.Should().Be("order-review");

        order = await host.TransitionAsync(order.Id, "reject", new { reason = "Stok yetersiz" }, Boss);

        order.Error.Should().BeNull();
        order.State.Should().Be("rejected");
        order.Data.ContainsKey("orderId").Should().BeFalse();
        (await bike.DbAsync(db => db.Orders.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task Escalated_order_is_created_when_approved_later()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var order = await host.StartInstanceAsync("order-approval", user: Alice);
        await host.TransitionAsync(order.Id, "submit-customer", new { customerId = bike.Ids.CustomerId, customerType = "Bireysel" }, Alice);
        await host.TransitionAsync(order.Id, "submit-order", new { storeId = bike.Ids.StoreId, productId = bike.Ids.ProductId, quantity = 6, deliveryType = "Ekspres" }, Alice);

        await host.RunJobsAsync(TimeSpan.FromMinutes(2));
        order = await host.GetAsync(order.Id);
        order.State.Should().Be("escalated");
        order.Layout.Should().Be("order-escalated");

        order = await host.TransitionAsync(order.Id, "approve", null, Boss);

        order.State.Should().Be("approved");
        var orderId = order.Data["orderId"]!.GetValue<int>();
        var row = await bike.DbAsync(db => db.Orders.SingleAsync(o => o.OrderId == orderId, TestContext.Current.CancellationToken));
        (row.RequiredDate - row.OrderDate).TotalDays.Should().Be(2, "express delivery");
    }

    [Fact]
    public async Task Order_for_a_store_without_staff_asks_for_a_correction()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var order = await host.StartInstanceAsync("order-approval", user: Alice);
        await host.TransitionAsync(order.Id, "submit-customer", new { customerId = bike.Ids.CustomerId, customerType = "Bireysel" }, Alice);
        order = await host.TransitionAsync(order.Id, "submit-order", new { storeId = bike.Ids.EmptyStoreId, productId = bike.Ids.ProductId, quantity = 1, deliveryType = "Standart" }, Alice);

        order.State.Should().Be("fix");
        order.Data["error"]!.GetValue<string>().Should().Be("Yeni mağazasında aktif personel yok: önce personel alımı akışıyla personel ekleyin.");

        await host.TransitionAsync(order.Id, "edit", null, Alice);
        order = await host.TransitionAsync(order.Id, "submit-order", new { storeId = bike.Ids.StoreId, productId = bike.Ids.ProductId, quantity = 1, deliveryType = "Standart" }, Alice);

        order.State.Should().Be("approved");
        (await bike.DbAsync(db => db.Orders.CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
    }

    [Fact]
    public async Task Cancelling_the_fulfillment_returns_the_reserved_stock_and_rejects_the_order()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var order = await host.StartInstanceAsync("order-approval", user: Alice);
        await host.TransitionAsync(order.Id, "submit-customer", new { customerId = bike.Ids.CustomerId, customerType = "Bireysel" }, Alice);
        order = await host.TransitionAsync(order.Id, "submit-order", new { storeId = bike.Ids.StoreId, productId = bike.Ids.ProductId, quantity = 1, deliveryType = "Standart" }, Alice);
        var orderId = order.Data["orderId"]!.GetValue<int>();
        var fulfillment = await host.GetAsync(Id(order.Data, "fulfillmentId"));
        fulfillment.State.Should().Be("picking");
        (await bike.StockAsync(bike.Ids.StoreId, bike.Ids.ProductId)).Should().Be(0);

        fulfillment = await host.TransitionAsync(fulfillment.Id, "cancel", new { reason = "Müşteri vazgeçti" }, Boss);

        fulfillment.State.Should().Be("cancelled");
        fulfillment.Status.Should().Be(WorkflowInstanceStatus.Cancelled);
        (await bike.StockAsync(bike.Ids.StoreId, bike.Ids.ProductId)).Should().Be(1);
        (await bike.DbAsync(db => db.Orders.Where(o => o.OrderId == orderId).Select(o => o.OrderStatus).SingleAsync(TestContext.Current.CancellationToken))).Should().Be(3);
    }

    [Fact]
    public async Task Adjust_stock_is_idempotent_and_never_goes_below_zero()
    {
        await using var bike = await Bike.StartAsync();
        await using var scope = bike.Host.Services.CreateAsyncScope();
        var data = new JsonObject();
        WorkflowTaskContext Context(int quantity, string operation) => new()
        {
            Task = new TaskDefinition
            {
                Task = "bike-adjust-stock",
                Config = new JsonObject { ["storeId"] = bike.Ids.StoreId, ["productId"] = bike.Ids.ProductId, ["quantity"] = quantity, ["operation"] = operation },
            },
            Data = data,
            Scope = new ExpressionScope { Data = data },
            Expressions = new WorkflowExpressionEngine(),
            InstanceId = Guid.NewGuid(),
            Workflow = "test",
            State = "test",
            Services = scope.ServiceProvider,
            Logger = NullLogger.Instance,
        };
        var task = new AdjustStockTask();

        await task.ExecuteAsync(Context(5, "added"), TestContext.Current.CancellationToken);
        await task.ExecuteAsync(Context(5, "added"), TestContext.Current.CancellationToken); // retry: marker present
        (await bike.StockAsync(bike.Ids.StoreId, bike.Ids.ProductId)).Should().Be(6);
        data["added"]!["quantity"]!.GetValue<int>().Should().Be(6);

        await task.ExecuteAsync(Context(-7, "taken"), TestContext.Current.CancellationToken);

        data["error"]!.GetValue<string>().Should().Be("Stok yetersiz: Merkez mağazasında \"Domane\" için 6 adet var, 7 adet gerekiyor.");
        data.ContainsKey("taken").Should().BeFalse();
        (await bike.StockAsync(bike.Ids.StoreId, bike.Ids.ProductId)).Should().Be(6);
    }

    [Fact]
    public async Task Stock_list_rows_carry_store_and_product_names_and_filter_by_store()
    {
        await using var bike = await Bike.StartAsync();

        var result = await bike.DbAsync(db => new Middt.Sample.Api.Repository.Bike.StockRepository(db).GetItemsAsync(
            new Middt.Framework.Common.Model.Data.BaseSearchRequestModel<Stock> { RequestModel = new Stock { StoreId = bike.Ids.StoreId }, RequestItemSize = 10 },
            new Middt.Sample.Api.Repository.Bike.StockRepository(db).GetAll(),
            TestContext.Current.CancellationToken));

        result.Count.Should().Be(1);
        result.Data.Should().ContainSingle(s => s.StoreName == "Merkez" && s.ProductName == "Domane" && s.Quantity == 1);
    }

    [Fact]
    public async Task Lookup_lists_start_with_the_newest_customers_and_products()
    {
        // getall (lookups) is capped at MaxGetAllItems: records created by the workflows must come first.
        await using var bike = await Bike.StartAsync();
        await bike.DbAsync(async db =>
        {
            db.Customers.Add(new Customer { FirstName = "Çağrı", LastName = "Öztürk", Email = "cagri@bike.test" });
            db.Products.Add(new Product { ProductName = "Yeni", BrandId = bike.Ids.BrandId, CategoryId = bike.Ids.CategoryId, ModelYear = 2026, ListPrice = 1 });
            return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        var customers = await bike.DbAsync(db => new Middt.Sample.Api.Repository.Bike.CustomerRepository(db).GetAll().ToListAsync(TestContext.Current.CancellationToken));
        var products = await bike.DbAsync(db => new Middt.Sample.Api.Repository.Bike.ProductRepository(db).GetAll().ToListAsync(TestContext.Current.CancellationToken));

        customers[0].LastName.Should().Be("Öztürk");
        customers.Select(c => c.CustomerId).Should().BeInDescendingOrder();
        products[0].ProductName.Should().Be("Yeni");
    }

    [Theory]
    [InlineData("""{ "storeId": 1, "productId": 1, "quantity": 1 }""", "'operation' is required.")]
    [InlineData("""{ "storeId": 1, "productId": 1, "quantity": 1, "operation": "a b" }""", "'operation' must be a data path (letters, digits, dots).")]
    public void Adjust_stock_config_is_validated_when_definitions_load(string config, string error)
    {
        var task = new TaskDefinition { Task = "bike-adjust-stock", Config = JsonNode.Parse(config)!.AsObject() };

        new AdjustStockTask().Validate(task, new WorkflowExpressionEngine()).Should().Contain(error);
    }

    [Fact]
    public void Order_status_must_be_known()
    {
        var task = new TaskDefinition { Task = "bike-set-order-status", Config = new JsonObject { ["orderId"] = 1, ["status"] = 9 } };

        new SetOrderStatusTask().Validate(task, new WorkflowExpressionEngine()).Should().Contain("'status' must be between 1 and 4.");
    }

    // ───────────────────────────── lifecycles (change / close / adopt) ─────────────────────────────

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Starts the lifecycle of an existing record like the list's row action: key "{entity}-{id}" + the id.</summary>
    private static Task<(WorkflowInstanceModel Instance, bool Created)> AdoptAsync(Bike bike, string workflow, string entity, string idField, int id) =>
        bike.Host.StartWithFlagAsync(workflow, new JsonObject { [idField] = id }, key: $"{entity}-{id}", user: Boss);

    private static TController Controller<TController>(IServiceProvider services, TController controller)
        where TController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services },
        };
        return controller;
    }

    [Fact]
    public async Task Adopting_an_existing_record_by_key_returns_the_same_lifecycle_instance()
    {
        await using var bike = await Bike.StartAsync();

        var (first, created) = await AdoptAsync(bike, "customer-onboarding", "customer", "customerId", bike.Ids.CustomerId);
        var (second, createdAgain) = await AdoptAsync(bike, "customer-onboarding", "customer", "customerId", bike.Ids.CustomerId);

        created.Should().BeTrue();
        first.WorkflowVersion.Should().Be(2);
        first.State.Should().Be("active");
        first.Key.Should().Be($"customer-{bike.Ids.CustomerId}");
        first.Data["customer"]!["email"]!.GetValue<string>().Should().Be("ali@bike.test");
        first.Data["customer"]!["active"]!.GetValue<bool>().Should().BeTrue();
        first.Data["change"]!["firstName"]!.GetValue<string>().Should().Be("Ali");
        first.Data.ContainsKey("adoptedAt").Should().BeTrue();
        createdAgain.Should().BeFalse();
        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task Adopting_without_a_key_sets_it_and_a_second_adoption_of_the_record_is_refused()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;

        var first = await host.StartInstanceAsync("category-request", new { categoryId = bike.Ids.CategoryId }, user: Boss);
        var second = await host.StartInstanceAsync("category-request", new { categoryId = bike.Ids.CategoryId }, user: Boss);

        first.Key.Should().Be($"category-{bike.Ids.CategoryId}");
        first.State.Should().Be("active");
        second.Status.Should().Be(WorkflowInstanceStatus.Faulted, "one lifecycle per record (setKey conflict)");
        second.Key.Should().BeNull();
        (await host.IncidentsAsync(second.Id)).Should().ContainSingle().Which.Error.Should()
            .Contain($"business key 'category-{bike.Ids.CategoryId}' is already used by another instance of 'category-request'");
        (await host.StartWithFlagAsync("category-request", key: $"category-{bike.Ids.CategoryId}", user: Boss)).Instance.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task Adopting_a_missing_record_ends_as_not_found()
    {
        await using var bike = await Bike.StartAsync();

        var (instance, _) = await AdoptAsync(bike, "store-opening", "store", "storeId", 999);

        instance.State.Should().Be("not-found");
        instance.StateSubType.Should().Be("Error");
        instance.Data["error"]!.GetValue<string>().Should().Be("Kayıt bulunamadı.");
    }

    [Fact]
    public async Task Approved_change_request_updates_the_record_and_reloads_it()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;
        var (store, _) = await AdoptAsync(bike, "store-opening", "store", "storeId", bike.Ids.StoreId);
        store.Data["change"]!["storeName"]!.GetValue<string>().Should().Be("Merkez");

        store = await host.TransitionAsync(store.Id, "request-change", new { storeName = "Merkez Kadıköy", city = "İstanbul", phone = "0216 555 00 00" }, Boss);
        store.State.Should().Be("change-approval");
        store.StateTitle.Should().Be("Değişiklik onayı");
        store.AvailableTransitions.Select(t => t.Key).Should().BeEquivalentTo("approve-change", "reject-change", "withdraw-change");
        (await bike.DbAsync(db => db.Stores.SingleAsync(s => s.StoreId == bike.Ids.StoreId, Ct))).StoreName.Should().Be("Merkez", "nothing changes before the approval");

        store = await host.TransitionAsync(store.Id, "approve-change", new { comment = "uygun" }, Boss);

        store.State.Should().Be("active");
        store.Data["store"]!["storeName"]!.GetValue<string>().Should().Be("Merkez Kadıköy");
        store.Data["change"]!["phone"]!.GetValue<string>().Should().Be("0216 555 00 00");
        store.Data["changeReview"]!["approvedBy"]!.GetValue<string>().Should().Be("boss");
        var row = await bike.DbAsync(db => db.Stores.SingleAsync(s => s.StoreId == bike.Ids.StoreId, Ct));
        row.StoreName.Should().Be("Merkez Kadıköy");
        row.City.Should().Be("İstanbul");
        row.Phone.Should().Be("0216 555 00 00");
        row.Active.Should().BeTrue();
    }

    [Fact]
    public async Task Rejected_change_request_keeps_the_record_and_prefills_the_old_values()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;
        var (product, _) = await AdoptAsync(bike, "product-launch", "product", "productId", bike.Ids.ProductId);

        await host.TransitionAsync(product.Id, "request-change", new
        {
            categoryId = bike.Ids.CategoryId, brandId = bike.Ids.BrandId, productName = "Domane SL", modelYear = 2026, listPrice = 2000,
        }, Boss);
        product = await host.TransitionAsync(product.Id, "reject-change", new { reason = "Fiyat yüksek" }, Boss);

        product.State.Should().Be("active");
        product.Data["change"]!["productName"]!.GetValue<string>().Should().Be("Domane");
        product.Data["change"]!["listPrice"]!.GetValue<decimal>().Should().Be(1000m);
        product.Data["changeReview"]!["reason"]!.GetValue<string>().Should().Be("Fiyat yüksek");
        (await bike.DbAsync(db => db.Products.SingleAsync(p => p.ProductId == bike.Ids.ProductId, Ct))).ListPrice.Should().Be(1000m);
    }

    [Fact]
    public async Task Change_violating_a_rule_asks_for_a_correction_with_the_tried_values()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;
        var (staff, _) = await AdoptAsync(bike, "staff-onboarding", "staff", "staffId", bike.Ids.StaffId);
        staff.Data["staff"]!["storeName"]!.GetValue<string>().Should().Be("Merkez");
        var onboarding = await host.StartInstanceAsync("staff-onboarding", new { storeId = bike.Ids.StoreId }, user: Boss);
        await host.TransitionAsync(onboarding.Id, "submit-personal", new { firstName = "Eda", lastName = "Kaya", email = "eda@bike.test" }, Boss);
        await host.TransitionAsync(onboarding.Id, "approve", null, Boss);

        await host.TransitionAsync(staff.Id, "request-change", new { storeId = bike.Ids.EmptyStoreId, firstName = "Mia", lastName = "Manager", email = "eda@bike.test" }, Boss);
        staff = await host.TransitionAsync(staff.Id, "approve-change", null, Boss);

        staff.State.Should().Be("change-fix");
        staff.Layout.Should().Be("bike-task-error");
        staff.Data["error"]!.GetValue<string>().Should().Be("eda@bike.test e-posta adresiyle kayıtlı bir personel var.");

        staff = await host.TransitionAsync(staff.Id, "back-to-record", null, Boss);
        staff.State.Should().Be("active");
        staff.Data["change"]!["email"]!.GetValue<string>().Should().Be("eda@bike.test", "the form shows the values just tried");

        await host.TransitionAsync(staff.Id, "request-change", new { storeId = bike.Ids.EmptyStoreId, firstName = "Mia", lastName = "Manager", email = "mia.manager@bike.test" }, Boss);
        staff = await host.TransitionAsync(staff.Id, "approve-change", null, Boss);

        staff.State.Should().Be("active");
        staff.Data["staff"]!["storeName"]!.GetValue<string>().Should().Be("Yeni");
        var row = await bike.DbAsync(db => db.Staffs.SingleAsync(s => s.StaffId == bike.Ids.StaffId, Ct));
        row.StoreId.Should().Be(bike.Ids.EmptyStoreId);
        row.Email.Should().Be("mia.manager@bike.test");
        row.Phone.Should().BeNull();
    }

    [Fact]
    public async Task Closing_deactivates_the_record_hides_it_from_lookups_and_blocks_new_orders()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;
        var (customer, _) = await AdoptAsync(bike, "customer-onboarding", "customer", "customerId", bike.Ids.CustomerId);

        customer = await host.TransitionAsync(customer.Id, "request-close", new { reason = "Müşteri ayrıldı" }, Boss);
        customer.State.Should().Be("close-approval");
        customer = await host.TransitionAsync(customer.Id, "reject-close", new { reason = "Açık siparişi var" }, Boss);
        customer.State.Should().Be("active");
        await host.TransitionAsync(customer.Id, "request-close", new { reason = "Müşteri ayrıldı" }, Boss);
        customer = await host.TransitionAsync(customer.Id, "approve-close", new { comment = "tamam" }, Boss);

        customer.State.Should().Be("closed");
        customer.StateTitle.Should().Be("Kapatıldı");
        customer.StateSubType.Should().Be("Success");
        customer.Status.Should().Be(WorkflowInstanceStatus.Completed);
        customer.Data["customer"]!["active"]!.GetValue<bool>().Should().BeFalse();
        customer.Data["closeReview"]!["approvedBy"]!.GetValue<string>().Should().Be("boss");
        customer.Data["closeReview"]!["reason"].Should().BeNull("the earlier rejection was reset by the new request");
        (await bike.DbAsync(db => db.Customers.SingleAsync(c => c.CustomerId == bike.Ids.CustomerId, Ct))).Active.Should().BeFalse("soft delete");

        // Lookups (getall) offer active records only; the list (getitems) shows every record and filters by status.
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BikeStoresContext>();
        var controller = Controller(scope.ServiceProvider, new Middt.Sample.Api.Controllers.CustomerController(new Middt.Sample.Api.Repository.Bike.CustomerRepository(db)));
        (await controller.GetAll(Ct)).Value!.Data.Should().BeEmpty();
        var all = (await controller.GetItems(new Middt.Framework.Common.Model.Data.BaseSearchRequestModel<Customer> { RequestModel = new Customer() }, Ct)).Value!;
        all.Data.Should().ContainSingle(c => c.CustomerId == bike.Ids.CustomerId && !c.Active);
        var passive = (await controller.GetItems(new Middt.Framework.Common.Model.Data.BaseSearchRequestModel<Customer> { RequestModel = new Customer { Status = "Pasif" } }, Ct)).Value!;
        passive.Count.Should().Be(1);
        var active = (await controller.GetItems(new Middt.Framework.Common.Model.Data.BaseSearchRequestModel<Customer> { RequestModel = new Customer { Status = "Aktif" } }, Ct)).Value!;
        active.Count.Should().Be(0);

        // A new order for the closed customer is refused with a correction step.
        var order = await host.StartInstanceAsync("order-approval", user: Alice);
        await host.TransitionAsync(order.Id, "submit-customer", new { customerId = bike.Ids.CustomerId, customerType = "Bireysel" }, Alice);
        order = await host.TransitionAsync(order.Id, "submit-order", new { storeId = bike.Ids.StoreId, productId = bike.Ids.ProductId, quantity = 1, deliveryType = "Standart" }, Alice);
        order.State.Should().Be("fix");
        order.Data["error"]!.GetValue<string>().Should().Be("Müşteri Ali Veli pasif durumda.");

        // The closed lifecycle is final: starting with the key returns it.
        (await AdoptAsync(bike, "customer-onboarding", "customer", "customerId", bike.Ids.CustomerId)).Instance.Id.Should().Be(customer.Id);
    }

    [Fact]
    public async Task A_category_with_active_products_cannot_be_closed()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;
        var (category, _) = await AdoptAsync(bike, "category-request", "category", "categoryId", bike.Ids.CategoryId);

        await host.TransitionAsync(category.Id, "request-close", new { reason = "Kullanılmıyor" }, Boss);
        category = await host.TransitionAsync(category.Id, "approve-close", null, Boss);

        category.State.Should().Be("change-fix");
        category.Data["error"]!.GetValue<string>().Should().Be("Bu kategoride aktif ürünler var: önce ürünleri kapatın.");
        (await bike.DbAsync(db => db.Categories.SingleAsync(c => c.CategoryId == bike.Ids.CategoryId, Ct))).Active.Should().BeTrue();

        // Close the product first (it stays in existing orders), then the category.
        var (product, _) = await AdoptAsync(bike, "product-launch", "product", "productId", bike.Ids.ProductId);
        await host.TransitionAsync(product.Id, "request-close", new { reason = "Üretimden kalktı" }, Boss);
        (await host.TransitionAsync(product.Id, "approve-close", null, Boss)).State.Should().Be("closed");
        await host.TransitionAsync(category.Id, "back-to-record", null, Boss);
        await host.TransitionAsync(category.Id, "request-close", new { reason = "Kullanılmıyor" }, Boss);
        (await host.TransitionAsync(category.Id, "approve-close", null, Boss)).State.Should().Be("closed");

        // A closed category cannot be chosen for a new product.
        var launch = await host.StartInstanceAsync("product-launch", user: Alice);
        launch = await host.TransitionAsync(launch.Id, "submit", new
        {
            categoryId = bike.Ids.CategoryId, brandId = bike.Ids.BrandId, productName = "Checkpoint", modelYear = 2026, listPrice = 900, initialQuantity = 0,
        }, Boss);
        launch.State.Should().Be("fix");
        launch.Data["error"]!.GetValue<string>().Should().Be("Road Bikes kategorisi pasif durumda.");
    }

    [Fact]
    public async Task An_adopted_passive_record_can_be_reactivated_with_approval()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;
        await bike.DbAsync(db => db.Stores.Where(s => s.StoreId == bike.Ids.EmptyStoreId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Active, false), Ct));

        var (store, _) = await AdoptAsync(bike, "store-opening", "store", "storeId", bike.Ids.EmptyStoreId);
        store.State.Should().Be("inactive");
        store.StateTitle.Should().Be("Pasif");
        store.AvailableTransitions.Select(t => t.Key).Should().Equal("request-reactivate");

        // A passive store cannot get new staff.
        var onboarding = await host.StartInstanceAsync("staff-onboarding", new { storeId = bike.Ids.EmptyStoreId }, user: Boss);
        await host.TransitionAsync(onboarding.Id, "submit-personal", new { firstName = "Eda", lastName = "Kaya", email = "eda@bike.test" }, Boss);
        (await host.TransitionAsync(onboarding.Id, "approve", null, Boss)).Data["error"]!.GetValue<string>().Should().Be("Yeni mağazası pasif durumda.");

        await host.TransitionAsync(store.Id, "request-reactivate", new { reason = "Yeniden açılıyor" }, Boss);
        store = await host.TransitionAsync(store.Id, "approve-reactivate", null, Boss);

        store.State.Should().Be("active");
        store.Data["store"]!["active"]!.GetValue<bool>().Should().BeTrue();
        (await bike.DbAsync(db => db.Stores.SingleAsync(s => s.StoreId == bike.Ids.EmptyStoreId, Ct))).Active.Should().BeTrue();
    }

    [Fact]
    public async Task Write_endpoints_of_workflow_managed_records_are_closed()
    {
        await using var bike = await Bike.StartAsync();
        var mvc = new ServiceCollection().AddLogging().AddMvcCore().Services.BuildServiceProvider();
        await using var scope = bike.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BikeStoresContext>();
        var controller = Controller(mvc, new Middt.Sample.Api.Controllers.CategoryController(new Middt.Sample.Api.Repository.Bike.CategoryRepository(db)));
        controller.ControllerContext.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "manager")], "test"));

        foreach (var result in new[]
        {
            (await controller.Insert(new Category { CategoryName = "X" }, Ct)).Result,
            (await controller.Update(new Category { CategoryId = bike.Ids.CategoryId, CategoryName = "X" }, Ct)).Result,
            (await controller.Delete(new Category { CategoryId = bike.Ids.CategoryId }, Ct)).Result,
        })
        {
            var problem = result.Should().BeOfType<Microsoft.AspNetCore.Mvc.ObjectResult>().Subject;
            problem.StatusCode.Should().Be(403);
            problem.Value.Should().BeOfType<Microsoft.AspNetCore.Mvc.ProblemDetails>().Which.Detail.Should().Be("Bu kayıt iş akışı üzerinden değiştirilir.");
        }

        (await bike.DbAsync(db => db.Categories.Select(c => c.CategoryName).ToListAsync(Ct))).Should().Equal("Road Bikes");
    }

    [Fact]
    public async Task Test_subflow_loads_branch_staff_from_database_and_completes_successfully()
    {
        await using var bike = await Bike.StartAsync();
        var host = bike.Host;
        var ids = bike.Ids;

        var instance = await host.StartInstanceAsync("test-subflow", user: Alice);
        instance.State.Should().Be("choose-store");

        // Submit store: triggers fetch-staff-subflow which calls get-branch-staff
        instance = await host.TransitionAsync(instance.Id, "submit-store", new { branchId = ids.StoreId, purpose = "Personel Görevlendirme" }, Alice);

        instance.State.Should().Be("view-staff");
        instance.Data.Should().ContainKey("availableStaff");
        var items = instance.Data["availableStaff"]!["items"]!.AsArray();
        items.Should().NotBeEmpty();
        items[0]!["id"]!.GetValue<long>().Should().Be(ids.StaffId);

        // Submit staff assignment: finishes the workflow
        instance = await host.TransitionAsync(instance.Id, "complete", new { assignedStaffId = ids.StaffId, notes = "Görevlendirme tamamlandı" }, Alice);

        instance.State.Should().Be("completed");
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
    }
}
