using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Middt.Framework.Common.Database.Attributes;

namespace Middt.Framework.Tests.Data;

/// <summary>Search model + entity exercising every QueryFilterBuilder rule.</summary>
public class Product
{
    public int Id { get; set; }

    [QueryString(StringSearchType.StartsWith)]
    public string? Name { get; set; }

    [QueryString(StringSearchType.Equal)]
    public string? Code { get; set; }

    /// <summary>No attribute: default string search is Contains.</summary>
    public string? Description { get; set; }

    [QueryInt(IntSearchType.GreaterThanOrEqual)]
    public int Stock { get; set; }

    [QueryIsZero]
    public int? Rank { get; set; }

    public decimal Price { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool? IsActive { get; set; }

    public byte Level { get; set; }

    [QueryIsNoFilter]
    public string? Notes { get; set; }

    [NotMapped]
    [QueryDate(DateSearchType.GreaterThanOrEqual, nameof(CreatedAt))]
    public DateTime? CreatedFrom { get; set; }

    [NotMapped]
    [QueryDate(DateSearchType.LessThan, nameof(CreatedAt))]
    public DateTime? CreatedTo { get; set; }

    public Guid ExternalId { get; set; }

    public DateOnly ReleaseDate { get; set; }

    public ProductKind Kind { get; set; }

    /// <summary>Computed, read-only: must be ignored by the filter builder.</summary>
    public string DisplayName => $"{Name} [{Code}]";
}

public enum ProductKind
{
    None = 0,
    Fresh = 1,
    Dried = 2,
}

/// <summary>Entity with a non-rowid (string) key, used to observe primary-key fallback ordering.</summary>
public class Tag
{
    [Key]
    public string Code { get; set; } = string.Empty;

    public string? Label { get; set; }
}

public class ProductDbContext(DbContextOptions<ProductDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Tag> Tags => Set<Tag>();
}

/// <summary>SQLite in-memory database whose connection stays open for the lifetime of the test.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<ProductDbContext> options;

    public SqliteTestDatabase()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder<ProductDbContext>().UseSqlite(connection).Options;

        using var context = NewContext();
        context.Database.EnsureCreated();
        context.Products.AddRange(SeedProducts());
        context.SaveChanges();
    }

    public ProductDbContext NewContext() => new(options);

    public static readonly Guid AppleExternalId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IReadOnlyList<Product> SeedProducts() =>
    [
        new() { Id = 1, Name = "Apple", Code = "A1", Description = "fresh red fruit", Stock = 10, Rank = 0, Price = 1.50m, CreatedAt = new DateTime(2024, 1, 10), ReleaseDate = new DateOnly(2024, 1, 10), ExternalId = AppleExternalId, Kind = ProductKind.Fresh, IsActive = true, Level = 1, Notes = "x" },
        new() { Id = 2, Name = "Apricot", Code = "A2", Description = "dried fruit", Stock = 0, Rank = 1, Price = 2.00m, CreatedAt = new DateTime(2024, 2, 15), ReleaseDate = new DateOnly(2024, 2, 15), Kind = ProductKind.Dried, IsActive = false, Level = 2 },
        new() { Id = 3, Name = "Banana", Code = "B1", Description = "yellow fruit", Stock = 25, Rank = null, Price = 0.75m, CreatedAt = new DateTime(2024, 3, 20), ReleaseDate = new DateOnly(2024, 3, 20), Kind = ProductKind.Fresh, IsActive = true, Level = 0 },
        new() { Id = 4, Name = "Blueberry", Code = "B2", Description = "small berry", Stock = 5, Rank = 2, Price = 4.00m, CreatedAt = new DateTime(2024, 4, 5), ReleaseDate = new DateOnly(2024, 4, 5), Kind = ProductKind.Fresh, IsActive = null, Level = 1 },
        new() { Id = 5, Name = "Cherry", Code = "C1", Description = "red stone fruit", Stock = 50, Rank = 0, Price = 3.25m, CreatedAt = new DateTime(2024, 5, 1), ReleaseDate = new DateOnly(2024, 5, 1), Kind = ProductKind.Fresh, IsActive = true, Level = 3 },
    ];

    public void Dispose() => connection.Dispose();
}
