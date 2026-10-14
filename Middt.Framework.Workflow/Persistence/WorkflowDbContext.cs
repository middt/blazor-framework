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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite has no schemas.
        var schema = Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true ? null : workflowOptions.Value.Schema;

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
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.Ignore(x => x.IsTerminal);
            e.HasIndex(x => new { x.WorkflowKey, x.BusinessKey }).IsUnique();
            e.HasIndex(x => new { x.WorkflowKey, x.Status, x.State });
            e.HasIndex(x => x.ParentInstanceId);
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
