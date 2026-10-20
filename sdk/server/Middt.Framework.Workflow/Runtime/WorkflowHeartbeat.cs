using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Middt.Framework.Workflow.Persistence;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>
/// Keeps a lease alive while work runs: calls <c>beat</c> every <c>interval</c> on its own scope (the caller's
/// <see cref="WorkflowDbContext"/> stays busy with the work). Used for the Busy claim of an instance (so recovery only faults
/// instances whose process died, not ones whose task is merely slow) and for job leases.
/// </summary>
internal sealed partial class WorkflowHeartbeat : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;

    private WorkflowHeartbeat(
        IServiceScopeFactory scopeFactory,
        TimeSpan interval,
        TimeProvider time,
        ILogger logger,
        Func<WorkflowDbContext, DateTime, CancellationToken, Task> beat)
    {
        loop = interval <= TimeSpan.Zero ? Task.CompletedTask : RunAsync(scopeFactory, interval, time, logger, beat, stop.Token);
    }

    public static WorkflowHeartbeat Start(
        IServiceScopeFactory scopeFactory,
        TimeSpan interval,
        TimeProvider time,
        ILogger logger,
        Func<WorkflowDbContext, DateTime, CancellationToken, Task> beat) =>
        new(scopeFactory, interval, time, logger, beat);

    private static async Task RunAsync(
        IServiceScopeFactory scopeFactory,
        TimeSpan interval,
        TimeProvider time,
        ILogger logger,
        Func<WorkflowDbContext, DateTime, CancellationToken, Task> beat,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(interval, time);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
                    await beat(db, time.GetUtcNow().UtcDateTime, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogBeatFailed(logger, ex); // the next beat tries again; the lease still has time left
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // stopped
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await loop;
        stop.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workflow lease heartbeat failed")]
    private static partial void LogBeatFailed(ILogger logger, Exception exception);
}
