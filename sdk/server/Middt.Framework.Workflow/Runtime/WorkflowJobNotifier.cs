using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middt.Framework.Workflow.Persistence;
using Npgsql;

namespace Middt.Framework.Workflow.Runtime;

/// <summary>
/// Wakes the job processors of every node when jobs are written. On PostgreSQL a <c>NOTIFY</c> reaches the
/// <see cref="WorkflowJobListener"/> of all processes sharing the database; elsewhere only the local
/// <see cref="WorkflowJobSignal"/> is set and other nodes fall back to <see cref="MiddtWorkflowOptions.JobPollingInterval"/>.
/// </summary>
public sealed partial class WorkflowJobNotifier(WorkflowJobSignal signal, IOptions<MiddtWorkflowOptions> options, ILogger<WorkflowJobNotifier> logger)
{
    /// <summary>The <c>LISTEN</c>/<c>NOTIFY</c> channel; one per workflow schema, so two applications on one database stay apart.</summary>
    public string Channel { get; } = ChannelFor(options.Value.Schema);

    internal static string ChannelFor(string? schema) =>
        "middt_wf_jobs_" + Regex.Replace(string.IsNullOrEmpty(schema) ? "public" : schema, "[^A-Za-z0-9_]", "_").ToLowerInvariant();

    /// <summary>Wakes the local processor and, on PostgreSQL, every other node. Call after the jobs are committed.</summary>
    public async Task NotifyAsync(WorkflowDbContext db, CancellationToken cancellationToken)
    {
        signal.Notify();
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) != true)
        {
            return;
        }

        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_notify({Channel}, '')", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Other nodes still poll; a lost wake-up only costs latency.
            LogNotifyFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workflow job NOTIFY failed; other nodes fall back to polling")]
    private static partial void LogNotifyFailed(ILogger logger, Exception exception);
}

/// <summary>
/// PostgreSQL only: <c>LISTEN</c>s on <see cref="WorkflowJobNotifier.Channel"/> over a dedicated connection and wakes the
/// local job processor when any node writes jobs, so a job created on node A starts on node B without waiting for the
/// polling interval. Reconnects on failure and wakes the processor after every (re)connect to cover missed notifications.
/// </summary>
public sealed partial class WorkflowJobListener(
    IServiceScopeFactory scopeFactory,
    WorkflowJobNotifier notifier,
    WorkflowJobSignal signal,
    ILogger<WorkflowJobListener> logger) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    /// <summary>Set once the first <c>LISTEN</c> succeeded (tests wait for it).</summary>
    public Task Listening => listening.Task;

    private readonly TaskCompletionSource listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? connectionString;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) != true)
            {
                return;
            }

            connectionString = db.Database.GetConnectionString();
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, _) => signal.Notify();
                await using (var listen = new NpgsqlCommand($"LISTEN \"{notifier.Channel}\"", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                listening.TrySetResult();
                signal.Notify(); // jobs written while we were not listening
                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogListenFailed(logger, ex);
                await Task.Delay(ReconnectDelay, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workflow job LISTEN connection failed; reconnecting")]
    private static partial void LogListenFailed(ILogger logger, Exception exception);
}
