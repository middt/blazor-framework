using Microsoft.EntityFrameworkCore;

namespace Middt.Sample.Api.Model.Database;

/// <summary>
/// Idempotent startup upgrade of the BikeStores seed (data/database.sql), so existing and fresh databases end up alike
/// (SQL Server only):
/// <list type="bullet">
/// <item>The seed declares its text columns as <c>varchar</c>: with the default collation (code page 1252) Turkish letters
/// such as ı, ş, ğ entered in the workflows would be stored as '?'. They become <c>nvarchar</c> of the same length.</item>
/// <item>stores, categories, products and customers get <c>active bit NOT NULL DEFAULT 1</c> (staffs already has
/// <c>active</c>): the lifecycle workflows close a record by deactivating it, never by deleting it.</item>
/// </list>
/// </summary>
public static class BikeStoresSchemaUpgrade
{
    private const string Sql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        -- staffs.email carries an unnamed unique constraint, which blocks ALTER COLUMN: drop it, convert, re-create it.
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.staffs') AND name = N'email' AND TYPE_NAME(user_type_id) = N'varchar')
        BEGIN
            DECLARE @unique sysname = (
                SELECT TOP (1) kc.name FROM sys.key_constraints kc
                JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE kc.parent_object_id = OBJECT_ID(N'dbo.staffs') AND kc.type = 'UQ' AND c.name = N'email');
            IF @unique IS NOT NULL
            BEGIN
                DECLARE @drop NVARCHAR(400) = N'ALTER TABLE dbo.staffs DROP CONSTRAINT ' + QUOTENAME(@unique);
                EXEC sys.sp_executesql @drop;
            END
            ALTER TABLE dbo.staffs ALTER COLUMN email NVARCHAR(255) NOT NULL;
            ALTER TABLE dbo.staffs ADD CONSTRAINT UQ_staffs_email UNIQUE (email);
        END

        DECLARE @sql NVARCHAR(MAX) = N'';
        SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
            + N' ALTER COLUMN ' + QUOTENAME(c.name) + N' NVARCHAR(' + CAST(c.max_length AS NVARCHAR(10)) + N')'
            + CASE WHEN c.is_nullable = 1 THEN N' NULL' ELSE N' NOT NULL' END + N';'
        FROM sys.columns c
        JOIN sys.tables t ON t.object_id = c.object_id
        WHERE TYPE_NAME(c.user_type_id) = N'varchar'
          AND t.name IN (N'brands', N'categories', N'customers', N'products', N'staffs', N'stores');

        -- Soft delete: closed records are deactivated (orders keep referencing them).
        IF COL_LENGTH(N'dbo.stores', N'active') IS NULL
            SET @sql += N'ALTER TABLE dbo.stores ADD active BIT NOT NULL CONSTRAINT DF_stores_active DEFAULT (1);';
        IF COL_LENGTH(N'dbo.categories', N'active') IS NULL
            SET @sql += N'ALTER TABLE dbo.categories ADD active BIT NOT NULL CONSTRAINT DF_categories_active DEFAULT (1);';
        IF COL_LENGTH(N'dbo.products', N'active') IS NULL
            SET @sql += N'ALTER TABLE dbo.products ADD active BIT NOT NULL CONSTRAINT DF_products_active DEFAULT (1);';
        IF COL_LENGTH(N'dbo.customers', N'active') IS NULL
            SET @sql += N'ALTER TABLE dbo.customers ADD active BIT NOT NULL CONSTRAINT DF_customers_active DEFAULT (1);';

        IF @sql <> N''
        BEGIN
            EXEC sys.sp_executesql @sql;
            -- The view's metadata is bound to the column definitions of its base tables.
            IF OBJECT_ID(N'dbo.viewOrders', N'V') IS NOT NULL
                EXEC sys.sp_refreshview N'dbo.viewOrders';
        END

        COMMIT TRANSACTION;
        """;

    /// <summary>Applies the missing changes (SQL Server only; other providers are left alone).</summary>
    public static async Task ApplyAsync(BikeStoresContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync(Sql, cancellationToken);
        }
    }
}
