using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;

namespace Middt.Framework.Workflow.Persistence;

/// <summary>Workflow tables (instances, instance data, history, jobs, incidents). The provider is chosen by the application.</summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, IOptions<MiddtWorkflowOptions> workflowOptions) : DbContext(options)
{
    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();

    public DbSet<WorkflowInstanceData> InstanceData => Set<WorkflowInstanceData>();

    public DbSet<WorkflowHistory> History => Set<WorkflowHistory>();

    public DbSet<WorkflowJob> Jobs => Set<WorkflowJob>();

    public DbSet<WorkflowIncident> Incidents => Set<WorkflowIncident>();

    /// <summary>
    /// New time-ordered UUIDv7 key for workflow instances and jobs.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Called through the DbContext instance.")]
    public Guid NewId() => Guid.CreateVersion7();

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
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.Ignore(x => x.IsTerminal);
            e.Ignore(x => x.Data);
            e.Ignore(x => x.DataVersion);
            e.HasOne(x => x.InstanceData)
                .WithOne()
                .HasForeignKey<WorkflowInstanceData>(x => x.InstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.InstanceData).AutoInclude();
            e.HasIndex(x => new { x.WorkflowKey, x.BusinessKey }).IsUnique();
            e.HasIndex(x => new { x.WorkflowKey, x.Status, x.State });
            e.HasIndex(x => x.ParentInstanceId);

            // Instance list: WHERE WorkflowKey = @k [AND StartedBy = @owner] ORDER BY CreatedAt DESC.
            e.HasIndex(x => new { x.WorkflowKey, x.CreatedAt }, "IX_WorkflowInstances_WorkflowKey_CreatedAt").IsDescending(false, true);
            e.HasIndex(x => new { x.WorkflowKey, x.StartedBy, x.CreatedAt }, "IX_WorkflowInstances_WorkflowKey_StartedBy_CreatedAt").IsDescending(false, false, true);

            // Busy recovery scan: WHERE Status = 'Busy' AND BusySince < @threshold.
            e.HasIndex(x => new { x.Status, x.BusySince }, "IX_WorkflowInstances_Status_BusySince");
        });

        modelBuilder.Entity<WorkflowInstanceData>(e =>
        {
            e.ToTable("WorkflowInstanceData", schema);
            e.HasKey(x => x.InstanceId);
            e.Property(x => x.Data).IsRequired();
            if (isPostgres)
            {
                e.Property(x => x.Data).HasColumnType("jsonb");
            }

            e.Property(x => x.DataHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ETag).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.DataHash, "IX_WorkflowInstanceData_DataHash");
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

        if (isPostgres)
        {
            var dateTimeConverter = new ValueConverter<DateTime, DateTime>(
                v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            var nullableDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
                v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(dateTimeConverter);
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(nullableDateTimeConverter);
                    }
                }
            }
        }
    }
}
