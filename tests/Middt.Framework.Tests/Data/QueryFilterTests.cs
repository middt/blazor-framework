using Microsoft.EntityFrameworkCore;
using Middt.Framework.Common.Database;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Tests.Data;

public sealed class QueryFilterTests : IDisposable
{
    private readonly SqliteTestDatabase database = new();

    public void Dispose() => database.Dispose();

    private async Task<int[]> FilterIds(Product filter)
    {
        await using var context = database.NewContext();
        var repository = new BaseCrudRepository<Product, ProductDbContext>(context);
        var result = await repository.GetItemsAsync(
            new BaseSearchRequestModel<Product> { RequestModel = filter, RequestItemSize = 0 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Count.Should().Be(result.Data!.Count);
        return [.. result.Data.Select(p => p.Id).Order()];
    }

    [Fact]
    public async Task Empty_filter_returns_everything_because_defaults_nulls_and_zeros_are_skipped()
    {
        (await FilterIds(new Product())).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Null_filter_model_returns_query_unchanged()
    {
        await using var context = database.NewContext();
        var query = QueryFilterBuilder.ApplyFilter(context.Products.AsNoTracking(), null);
        (await query.CountAsync(TestContext.Current.CancellationToken)).Should().Be(5);
    }

    [Fact]
    public async Task Empty_strings_are_skipped()
    {
        (await FilterIds(new Product { Name = "", Code = "", Description = "" })).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task QueryString_StartsWith_does_not_match_inner_text()
    {
        (await FilterIds(new Product { Name = "Ap" })).Should().Equal(1, 2);
        (await FilterIds(new Product { Name = "pple" })).Should().BeEmpty();
    }

    [Fact]
    public async Task QueryString_Equal_requires_exact_value()
    {
        (await FilterIds(new Product { Code = "A" })).Should().BeEmpty();
        (await FilterIds(new Product { Code = "A1" })).Should().Equal(1);
    }

    [Fact]
    public async Task String_without_attribute_uses_Contains()
    {
        (await FilterIds(new Product { Description = "fruit" })).Should().Equal(1, 2, 3, 5);
    }

    [Fact]
    public async Task QueryInt_GreaterThanOrEqual_is_applied()
    {
        (await FilterIds(new Product { Stock = 10 })).Should().Equal(1, 3, 5);
    }

    [Fact]
    public async Task Zero_is_skipped_for_numbers_without_QueryIsZero()
    {
        // byte Level = 0 and int Stock = 0 mean "no filter"
        (await FilterIds(new Product { Level = 0, Stock = 0 })).Should().Equal(1, 2, 3, 4, 5);
        (await FilterIds(new Product { Level = 1 })).Should().Equal(1, 4);
    }

    [Fact]
    public async Task QueryIsZero_treats_zero_as_a_real_value_and_null_as_no_filter()
    {
        (await FilterIds(new Product { Rank = 0 })).Should().Equal(1, 5);
        (await FilterIds(new Product { Rank = null })).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Decimal_uses_equality()
    {
        (await FilterIds(new Product { Price = 2.00m })).Should().Equal(2);
    }

    [Fact]
    public async Task Nullable_bool_filters_only_when_set()
    {
        (await FilterIds(new Product { IsActive = true })).Should().Equal(1, 3, 5);
        (await FilterIds(new Product { IsActive = false })).Should().Equal(2);
    }

    [Fact]
    public async Task QueryIsNoFilter_property_is_ignored()
    {
        (await FilterIds(new Product { Notes = "does-not-exist" })).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Date_range_properties_target_the_mapped_column()
    {
        var ids = await FilterIds(new Product
        {
            CreatedFrom = new DateTime(2024, 2, 15),
            CreatedTo = new DateTime(2024, 4, 5), // LessThan: exclusive upper bound
        });

        ids.Should().Equal(2, 3);
    }

    [Fact]
    public async Task Computed_read_only_property_is_ignored()
    {
        // DisplayName is not mapped; if it were used as a filter EF could not translate the query.
        var filter = new Product { Name = "Ban" };
        filter.DisplayName.Should().NotBeNullOrEmpty();

        (await FilterIds(filter)).Should().Equal(3);
    }

    [Fact]
    public async Task Multiple_filters_are_combined_with_and()
    {
        (await FilterIds(new Product { Description = "fruit", IsActive = true, Stock = 20 })).Should().Equal(3, 5);
    }

    [Fact]
    public async Task Filter_values_are_parameters_not_expression_text()
    {
        // Would break / widen the query if the value were concatenated into the dynamic expression.
        (await FilterIds(new Product { Name = "\") || true || (\"" })).Should().BeEmpty();
        (await FilterIds(new Product { Code = "' OR '1'='1" })).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Name", "Name")]
    [InlineData("stock", "Stock")]
    [InlineData("CREATEDAT", "CreatedAt")]
    [InlineData("Name; DROP TABLE Products", null)]
    [InlineData("x => x", null)]
    [InlineData("Name desc, Id", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ResolveSortProperty_only_accepts_known_properties(string? input, string? expected)
    {
        QueryFilterBuilder.ResolveSortProperty<Product>(input).Should().Be(expected);
    }
}
