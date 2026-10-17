using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Middt.Framework.Workflow.Persistence;

/// <summary>Workflow tables (instances, history, jobs, incidents). The provider is chosen by the application.</summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, IOptions<MiddtWorkflowOptions> workflowOptions) : DbContext(options)
{
    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();

    public DbSet<WorkflowHistory> History => Set<WorkflowHistory>();

    public DbSet<WorkflowJob> Jobs => Set<WorkflowJob>();

    public DbSet<WorkflowIncident> Incidents => Set<WorkflowIncident>();

    private bool? isSqlServer;

    /// <summary>
    /// New key for instances and jobs, ordered by creation time in the provider's own GUID sort order. SQL Server compares
    /// <c>uniqueidentifier</c> starting with the last six bytes, so a UUIDv7 (time in the first bytes) would be random there
    /// and fragment the clustered primary key; it gets the timestamp in those bytes instead. Other providers use UUIDv7.
    /// </summary>
    internal Guid NewId()
    {
        isSqlServer ??= Database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal) == true;
        return isSqlServer.Value ? NewSqlServerSequentialId(DateTimeOffset.UtcNow) : Guid.CreateVersion7();
    }

    /// <summary>48-bit Unix milliseconds big-endian in bytes 10-15 (SQL Server's most significant), random elsewhere.</summary>
    internal static Guid NewSqlServerSequentialId(DateTimeOffset now)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes[..10]);
        Span<byte> time = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(time, now.ToUnixTimeMilliseconds());
        time[2..].CopyTo(bytes[10..]);
        return new Guid(bytes);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var provider = Database.ProviderName ?? string.Empty;
        // SQLite has no schemas.
        var schema = provider.Contains("Sqlite", StringComparison.Ordinal) ? null : workflowOptions.Value.Schema;
        var isPostgres = provider.Contains("Npgsql", StringComparison.Ordinal);

        modelBuilder.Entity<WorkflowInstance>(e =>
        {
            e.ToTable("WorkflowInstances", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkflowKey).HasMaxLength(128).IsRequired();
            e.Property(x => x.BusinessKey).HasMaxLength(256);
            e.Property(x => x.State).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.PendingTransition).HasMaxLength(128);
            e.Property(x => x.StartedBy).HasMaxLength(256);
            e.Property(x => x.LastExecutedBy).HasMaxLength(256);
            if (isPostgres)
            {
                e.Property(x => x.Data).HasColumnType("jsonb");
            }

            e.Property(x => x.Revision).IsConcurrencyToken();
            e.Ignore(x => x.IsTerminal);
            e.HasIndex(x => new { x.WorkflowKey, x.BusinessKey }).IsUnique();
            e.HasIndex(x => new { x.WorkflowKey, x.Status, x.State });
            e.HasIndex(x => x.ParentInstanceId);

            // Instance list: WHERE WorkflowKey = @k [AND StartedBy = @owner] ORDER BY CreatedAt DESC.
            e.HasIndex(x => new { x.WorkflowKey, x.CreatedAt }, "IX_WorkflowInstances_WorkflowKey_CreatedAt").IsDescending(false, true);
            e.HasIndex(x => new { x.WorkflowKey, x.StartedBy, x.CreatedAt }, "IX_WorkflowInstances_WorkflowKey_StartedBy_CreatedAt").IsDescending(false, false, true);

            // Busy recovery scan: WHERE Status = 'Busy' AND BusySince < @threshold.
            e.HasIndex(x => new { x.Status, x.BusySince }, "IX_WorkflowInstances_Status_BusySince");
        });

        modelBuilder.Entity<WorkflowHistory>(e =>
        {
            e.ToTable("WorkflowHistory", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.TransitionKey).HasMaxLength(128);
            e.Property(x => x.FromState).HasMaxLength(128);
            e.Property(x => x.ToState).HasMaxLength(128);
            e.Property(x => x.Trigger).HasMaxLength(16);
            e.Property(x => x.ExecutedBy).HasMaxLength(256);
            if (isPostgres)
            {
                e.Property(x => x.Input).HasColumnType("jsonb");
                e.Property(x => x.Data).HasColumnType("jsonb");
                e.Property(x => x.Tasks).HasColumnType("jsonb");
            }

            e.HasIndex(x => x.InstanceId);
        });

        modelBuilder.Entity<WorkflowJob>(e =>
        {
            e.ToTable("WorkflowJobs", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.TransitionKey).HasMaxLength(128);
            e.Property(x => x.State).HasMaxLength(128);
            e.Property(x => x.RequestedBy).HasMaxLength(256);
            if (isPostgres)
            {
                e.Property(x => x.Payload).HasColumnType("jsonb");
            }

            e.HasIndex(x => x.DueAt);
            e.HasIndex(x => x.InstanceId);
        });

        modelBuilder.Entity<WorkflowIncident>(e =>
        {
            e.ToTable("WorkflowIncidents", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.State).HasMaxLength(128);
            e.Property(x => x.TransitionKey).HasMaxLength(128);
            e.Property(x => x.TaskKey).HasMaxLength(128);
            e.HasIndex(x => x.InstanceId);
        });
    }
}
