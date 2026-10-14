using Microsoft.EntityFrameworkCore;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Tests.Data;

public sealed class RepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase database = new();

    public void Dispose() => database.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<BaseResponseDataModel<List<Product>>> Search(BaseSearchRequestModel<Product> model)
    {
        await using var context = database.NewContext();
        return await new BaseCrudRepository<Product, ProductDbContext>(context).GetItemsAsync(model, Ct);
    }

    [Fact]
    public async Task SortReverse_true_sorts_ascending()
    {
        var result = await Search(new() { SortProperty = "Stock", SortReverse = true, RequestItemSize = 0 });
        result.Data!.Select(p => p.Stock).Should().Equal(0, 5, 10, 25, 50);
    }

    [Fact]
    public async Task SortReverse_false_sorts_descending()
    {
        var result = await Search(new() { SortProperty = "stock", SortReverse = false, RequestItemSize = 0 });
        result.Data!.Select(p => p.Stock).Should().Equal(50, 25, 10, 5, 0);
    }

    [Theory]
    [InlineData("Name; DROP TABLE Products")]
    [InlineData("x => x")]
    [InlineData("Unknown")]
    public async Task Unknown_or_malicious_sort_property_is_ignored(string sortProperty)
    {
        var result = await Search(new() { SortProperty = sortProperty, RequestItemSize = 10 });

        result.IsSuccess.Should().BeTrue();
        result.Data!.Select(p => p.Id).Should().Equal(1, 2, 3, 4, 5); // primary-key fallback

        await using var context = database.NewContext();
        (await context.Products.CountAsync(Ct)).Should().Be(5);
    }

    [Theory]
    [InlineData(nameof(Product.DisplayName))] // computed, read-only
    [InlineData(nameof(Product.CreatedFrom))] // [NotMapped] range property
    public async Task Sort_by_unmapped_property_is_ignored_instead_of_failing_translation(string sortProperty)
    {
        var result = await Search(new() { SortProperty = sortProperty, SortReverse = true, RequestItemSize = 10 });

        result.IsSuccess.Should().BeTrue();
        result.Data!.Select(p => p.Id).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Paging_returns_total_count_and_requested_page()
    {
        var result = await Search(new() { SortProperty = "Stock", SortReverse = true, RequestItemSize = 2, CurrentPage = 2 });

        result.Count.Should().Be(5);
        result.Data!.Select(p => p.Stock).Should().Equal(10, 25);
    }

    [Fact]
    public async Task Paging_count_reflects_the_filter()
    {
        var result = await Search(new() { RequestModel = new Product { Description = "fruit" }, RequestItemSize = 3, CurrentPage = 2 });

        result.Count.Should().Be(4);
        result.Data!.Select(p => p.Id).Should().Equal(5);
    }

    [Fact]
    public async Task CurrentPage_below_one_is_treated_as_first_page()
    {
        var result = await Search(new() { RequestItemSize = 2, CurrentPage = 0 });
        result.Data!.Select(p => p.Id).Should().Equal(1, 2);
    }

    [Fact]
    public async Task RequestItemSize_zero_returns_all_rows()
    {
        var result = await Search(new() { RequestItemSize = 0, CurrentPage = 3 });

        result.Count.Should().Be(5);
        result.Data.Should().HaveCount(5);
    }

    [Fact]
    public async Task Unsorted_paging_falls_back_to_primary_key_order()
    {
        await using (var seed = database.NewContext())
        {
            // Inserted out of key order; SQLite scans a TEXT-keyed table in rowid (insertion) order.
            seed.Tags.AddRange(new Tag { Code = "c" }, new Tag { Code = "a" }, new Tag { Code = "b" });
            await seed.SaveChangesAsync(Ct);
        }

        await using var context = database.NewContext();
        var repository = new BaseCrudRepository<Tag, ProductDbContext>(context);

        var paged = await repository.GetItemsAsync(new BaseSearchRequestModel<Tag> { RequestItemSize = 10 }, Ct);
        paged.Data!.Select(t => t.Code).Should().Equal("a", "b", "c");

        var secondPage = await repository.GetItemsAsync(new BaseSearchRequestModel<Tag> { RequestItemSize = 1, CurrentPage = 2 }, Ct);
        secondPage.Data!.Select(t => t.Code).Should().Equal("b");
    }

    [Fact]
    public async Task Insert_and_SaveChanges_persist_entity()
    {
        await using (var context = database.NewContext())
        {
            var repository = new BaseCrudRepository<Product, ProductDbContext>(context);
            var product = new Product { Name = "Durian", Code = "D1", CreatedAt = DateTime.UtcNow };
            await repository.InsertAsync(product, Ct);
            (await repository.SaveChangesAsync(Ct)).Should().Be(1);
            product.Id.Should().BeGreaterThan(5);
        }

        await using var verify = database.NewContext();
        var found = await new BaseCrudRepository<Product, ProductDbContext>(verify).FirstOrDefaultAsync(p => p.Code == "D1", Ct);
        found.Should().NotBeNull();
        found!.Name.Should().Be("Durian");
    }

    [Fact]
    public async Task Update_of_detached_entity_is_persisted()
    {
        await using (var context = database.NewContext())
        {
            var repository = new BaseCrudRepository<Product, ProductDbContext>(context);
            var detached = SqliteTestDatabase.SeedProducts()[0];
            detached.Name = "Green Apple";
            repository.Update(detached);
            await repository.SaveChangesAsync(Ct);
        }

        await using var verify = database.NewContext();
        (await verify.Products.SingleAsync(p => p.Id == 1, Ct)).Name.Should().Be("Green Apple");
    }

    [Fact]
    public async Task Delete_of_detached_entity_attaches_and_removes_it()
    {
        await using (var context = database.NewContext())
        {
            var repository = new BaseCrudRepository<Product, ProductDbContext>(context);
            repository.Delete(new Product { Id = 2 });
            (await repository.SaveChangesAsync(Ct)).Should().Be(1);
        }

        await using var verify = database.NewContext();
        var repo = new BaseCrudRepository<Product, ProductDbContext>(verify);
        (await repo.AnyAsync(p => p.Id == 2, Ct)).Should().BeFalse();
        (await repo.GetByIdAsync(2, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_of_tracked_entity_removes_it()
    {
        await using var context = database.NewContext();
        var repository = new BaseCrudRepository<Product, ProductDbContext>(context);
        var tracked = await repository.GetByIdAsync(3, Ct);

        repository.Delete(tracked!);
        await repository.SaveChangesAsync(Ct);

        (await repository.AnyAsync(p => p.Id == 3, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task GetAll_and_FindBy_do_not_track_entities()
    {
        await using var context = database.NewContext();
        var repository = new BaseCrudRepository<Product, ProductDbContext>(context);

        (await repository.GetAll().ToListAsync(Ct)).Should().HaveCount(5);
        (await repository.FindBy(p => p.Stock > 20).ToListAsync(Ct)).Select(p => p.Id).Should().BeEquivalentTo([3, 5]);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task FromSql_sends_interpolated_values_as_parameters()
    {
        await using var context = database.NewContext();
        var repository = new BaseCrudRepository<Product, ProductDbContext>(context);

        var name = "Apple";
        (await repository.FromSql($"SELECT * FROM Products WHERE Name = {name}").ToListAsync(Ct)).Should().ContainSingle();

        var injection = "x' OR '1'='1";
        (await repository.FromSql($"SELECT * FROM Products WHERE Name = {injection}").ToListAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteUpdateAsync_updates_matching_rows_in_the_database()
    {
        await using (var context = database.NewContext())
        {
            var repository = new BaseCrudRepository<Product, ProductDbContext>(context);
            var affected = await repository.ExecuteUpdateAsync(p => p.Stock < 10, s => s.SetProperty(p => p.Stock, 99), Ct);
            affected.Should().Be(2);
        }

        await using var verify = database.NewContext();
        (await verify.Products.Where(p => p.Stock == 99).Select(p => p.Id).OrderBy(id => id).ToListAsync(Ct)).Should().Equal(2, 4);
    }

    [Fact]
    public async Task ExecuteDeleteAsync_deletes_matching_rows_in_the_database()
    {
        await using (var context = database.NewContext())
        {
            var repository = new BaseCrudRepository<Product, ProductDbContext>(context);
            (await repository.ExecuteDeleteAsync(p => p.IsActive == true, Ct)).Should().Be(3);
        }

        await using var verify = database.NewContext();
        (await verify.Products.Select(p => p.Id).OrderBy(id => id).ToListAsync(Ct)).Should().Equal(2, 4);
    }

    [Fact]
    public async Task Transaction_rollback_discards_changes()
    {
        await using (var context = database.NewContext())
        {
            var repository = new BaseCrudRepository<Product, ProductDbContext>(context);
            await using var transaction = await repository.BeginTransactionAsync(Ct);
            await repository.InsertAsync(new Product { Name = "Temp", CreatedAt = DateTime.UtcNow }, Ct);
            await repository.SaveChangesAsync(Ct);
            await transaction.RollbackAsync(Ct);
        }

        await using var verify = database.NewContext();
        (await verify.Products.CountAsync(Ct)).Should().Be(5);
    }
}
