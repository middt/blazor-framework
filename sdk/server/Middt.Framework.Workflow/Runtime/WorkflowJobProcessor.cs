using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Diagnostics;
using Middt.Framework.Workflow.Persistence;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>
/// Processes due jobs (scheduled transitions, timeouts, async transitions, subflow work, event delivery) and recovers instances left
/// Busy by a crashed process. Safe to run on several instances: jobs are claimed with a conditional UPDATE (lease).
/// </summary>
public sealed partial class WorkflowJobProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<MiddtWorkflowOptions> options,
    WorkflowJobSignal signal,
    TimeProvider time,
    ILogger<WorkflowJobProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private const int BatchSize = 20;
    private const int MaxAttempts = 20;

    // Ticks of the last Busy-recovery scan (the processor is a singleton; RunOnceAsync may be called concurrently by tests).
    private long lastRecoveryTicks = DateTime.MinValue.Ticks;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await RunOnceAsync(stoppingToken) == BatchSize)
                {
                    // keep draining while full batches come back
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogLoopFailed(logger, ex);
            }

            await signal.WaitAsync(await NextWaitAsync(stoppingToken), stoppingToken);
        }
    }

    /// <summary>Sleeps until the next job is due (bounded by the polling interval).</summary>
    private async Task<TimeSpan> NextWaitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var next = await db.Jobs.AsNoTracking().Where(j => j.LockedUntil == null).OrderBy(j => j.DueAt).Select(j => (DateTime?)j.DueAt).FirstOrDefaultAsync(cancellationToken);
            if (next is { } due)
            {
                var wait = due - time.GetUtcNow().UtcDateTime;
                return wait < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : wait < options.Value.JobPollingInterval ? wait : options.Value.JobPollingInterval;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogLoopFailed(logger, ex);
        }

        return options.Value.JobPollingInterval;
    }

    /// <summary>Processes one batch of due jobs; returns how many were picked up.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var now = time.GetUtcNow().UtcDateTime;

        // Every job signal triggers a run; the recovery scan only needs to happen now and then.
        var last = Interlocked.Read(ref lastRecoveryTicks);
        if (now.Ticks - last >= options.Value.BusyRecoveryInterval.Ticks
            && Interlocked.CompareExchange(ref lastRecoveryTicks, now.Ticks, last) == last)
        {
            await RecoverStuckInstancesAsync(db, now, cancellationToken);
        }

        var due = await db.Jobs.AsNoTracking()
            .Where(j => j.DueAt <= now && (j.LockedUntil == null || j.LockedUntil < now))
            .OrderBy(j => j.DueAt)
            .Select(j => j.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var id in due)
        {
            var leaseUntil = now + Lease;
            var claimed = await db.Jobs
                .Where(j => j.Id == id && (j.LockedUntil == null || j.LockedUntil < now))
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.LockedUntil, leaseUntil).SetProperty(j => j.Attempts, j => j.Attempts + 1), cancellationToken);

            if (claimed == 1)
            {
                await ProcessAsync(id, cancellationToken);
            }
        }

        return due.Count;
    }

    private async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var activity = WorkflowDiagnostics.ActivitySource.StartActivity("workflow.job");
        activity?.SetTag("middt.workflow.job_id", jobId.ToString());

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        if (activity is not null)
        {
            activity.DisplayName = $"workflow.job {job.Kind}";
            activity.SetTag("middt.workflow.job_kind", job.Kind.ToString());
            activity.SetTag("middt.workflow.instance_id", job.InstanceId.ToString());
        }

        string? error = null;
        bool done;
        try
        {
            var outcome = await engine.ProcessJobAsync(job, cancellationToken);
            if (outcome == WorkflowEngine.JobOutcome.Postpone)
            {
                var later = time.GetUtcNow().UtcDateTime.AddMinutes(1);
                await db.Jobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.DueAt, later)
                    .SetProperty(j => j.LockedUntil, (DateTime?)null)
                    .SetProperty(j => j.Attempts, 0), cancellationToken);
                return;
            }

            done = outcome == WorkflowEngine.JobOutcome.Done;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Not retryable (transition no longer available, invalid input, missing instance ...): drop the job.
            // Unexpected failures fault the instance (with an incident and a retryable pending transition).
            LogJobFailed(logger, ex, job.Kind, job.InstanceId);
            error = ex.Message;
            done = job.Kind is not (WorkflowJobKind.ResumeParent or WorkflowJobKind.CancelChild or WorkflowJobKind.Event);
        }

        db.ChangeTracker.Clear();
        if (done || job.Attempts >= MaxAttempts)
        {
            await db.Jobs.Where(j => j.Id == jobId).ExecuteDeleteAsync(cancellationToken);
            if (!done)
            {
                db.Incidents.Add(new WorkflowIncident
                {
                    InstanceId = job.InstanceId,
                    TransitionKey = job.TransitionKey,
                    Error = $"{job.Kind} job abandoned after {job.Attempts} attempts: {error ?? "instance stayed busy"}",
                    Attempts = job.Attempts,
                    CreatedAt = time.GetUtcNow().UtcDateTime,
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        // Retry with backoff (busy instance or transient failure).
        var retryAt = time.GetUtcNow().UtcDateTime.AddMilliseconds(Math.Min(60_000, 250 * (1 << Math.Min(job.Attempts, 8))));
        await db.Jobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.DueAt, retryAt)
            .SetProperty(j => j.LockedUntil, (DateTime?)null)
            .SetProperty(j => j.LastError, error), cancellationToken);
    }

    private async Task RecoverStuckInstancesAsync(WorkflowDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var threshold = now - options.Value.BusyTimeout;
        var candidates = await db.Instances.AsNoTracking()
            .Where(i => i.Status == WorkflowInstanceStatus.Busy && i.BusySince < threshold)
            .Select(i => new { i.Id, i.State, i.PendingTransition })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        const string message = "Execution was interrupted (process stopped while the instance was busy).";
        var recovered = 0;
        foreach (var candidate in candidates)
        {
            var changed = await db.Instances
                .Where(i => i.Id == candidate.Id && i.Status == WorkflowInstanceStatus.Busy && i.BusySince < threshold)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, WorkflowInstanceStatus.Faulted)
                    .SetProperty(i => i.BusySince, (DateTime?)null)
                    .SetProperty(i => i.Revision, i => i.Revision + 1)
                    .SetProperty(i => i.Error, message), cancellationToken);
            if (changed == 1)
            {
                recovered++;
                db.Incidents.Add(new WorkflowIncident
                {
                    InstanceId = candidate.Id,
                    State = candidate.State,
                    TransitionKey = candidate.PendingTransition,
                    Error = message,
                    Attempts = 1,
                    CreatedAt = now,
                });
            }
        }

        if (recovered > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            LogRecovered(logger, recovered);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Workflow job loop failed")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workflow {Kind} job for instance {InstanceId} failed")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, WorkflowJobKind kind, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} interrupted workflow instance(s) marked Faulted")]
    private static partial void LogRecovered(ILogger logger, int count);
}
