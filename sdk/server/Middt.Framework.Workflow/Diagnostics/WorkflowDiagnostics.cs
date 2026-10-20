using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Middt.Framework.Workflow.Diagnostics;

/// <summary>
/// OpenTelemetry <see cref="ActivitySource"/> and <see cref="Meter"/> instruments for the Middt workflow engine.
/// </summary>
public static class WorkflowDiagnostics
{
    /// <summary>Name of the workflow engine <see cref="ActivitySource"/> and <see cref="Meter"/>.</summary>
    public const string SourceName = "Middt.Framework.Workflow";

    /// <summary>Version of the workflow instrumentation.</summary>
    public const string Version = "1.0.0";

    /// <summary><see cref="ActivitySource"/> for workflow transitions, tasks, and background jobs.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    /// <summary><see cref="Meter"/> for workflow engine metrics.</summary>
    public static readonly Meter Meter = new(SourceName, Version);

    /// <summary>Number of workflow instances started.</summary>
    public static readonly Counter<long> InstancesStarted = Meter.CreateCounter<long>(
        "middt.workflow.instances.started",
        unit: "{instance}",
        description: "Number of workflow instances started.");

    /// <summary>Number of workflow transitions executed.</summary>
    public static readonly Counter<long> TransitionsExecuted = Meter.CreateCounter<long>(
        "middt.workflow.transitions.executed",
        unit: "{transition}",
        description: "Number of workflow state transitions executed.");

    /// <summary>Duration of workflow transitions in milliseconds.</summary>
    public static readonly Histogram<double> TransitionDuration = Meter.CreateHistogram<double>(
        "middt.workflow.transitions.duration",
        unit: "ms",
        description: "Duration of workflow state transitions in milliseconds.");

    /// <summary>Number of workflow pipeline tasks executed.</summary>
    public static readonly Counter<long> TasksExecuted = Meter.CreateCounter<long>(
        "middt.workflow.tasks.executed",
        unit: "{task}",
        description: "Number of workflow pipeline tasks executed.");

    /// <summary>Duration of workflow pipeline tasks in milliseconds.</summary>
    public static readonly Histogram<double> TaskDuration = Meter.CreateHistogram<double>(
        "middt.workflow.tasks.duration",
        unit: "ms",
        description: "Duration of workflow pipeline tasks in milliseconds.");

    /// <summary>Number of background workflow jobs processed.</summary>
    public static readonly Counter<long> JobsProcessed = Meter.CreateCounter<long>(
        "middt.workflow.jobs.processed",
        unit: "{job}",
        description: "Number of background workflow jobs processed.");

    internal static Activity? StartTransitionActivity(
        string workflowKey,
        int workflowVersion,
        Guid instanceId,
        string? fromState,
        string transitionKey,
        string trigger)
    {
        var activity = ActivitySource.StartActivity($"workflow.transition {workflowKey}/{transitionKey}", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("middt.workflow.key", workflowKey);
            activity.SetTag("middt.workflow.version", workflowVersion);
            activity.SetTag("middt.workflow.instance_id", instanceId.ToString());
            if (!string.IsNullOrEmpty(fromState))
            {
                activity.SetTag("middt.workflow.from_state", fromState);
            }

            activity.SetTag("middt.workflow.transition", transitionKey);
            activity.SetTag("middt.workflow.trigger", trigger);
        }

        return activity;
    }

    internal static void CompleteTransitionActivity(
        Activity? activity,
        string workflowKey,
        string transitionKey,
        string trigger,
        string? toState,
        string status,
        string outcome,
        double durationMs,
        string? error = null)
    {
        if (activity is not null)
        {
            if (!string.IsNullOrEmpty(toState))
            {
                activity.SetTag("middt.workflow.to_state", toState);
            }

            activity.SetTag("middt.workflow.status", status);
            activity.SetTag("middt.workflow.outcome", outcome);
            if (!string.IsNullOrEmpty(error))
            {
                activity.SetStatus(ActivityStatusCode.Error, error);
            }
        }

        var tags = new TagList
        {
            { "workflow", workflowKey },
            { "transition", transitionKey },
            { "trigger", trigger },
            { "outcome", outcome },
        };
        TransitionsExecuted.Add(1, tags);
        TransitionDuration.Record(durationMs, tags);
        if (trigger == "Start")
        {
            InstancesStarted.Add(1, new TagList { { "workflow", workflowKey }, { "outcome", outcome } });
        }
    }

    internal static Activity? StartTaskActivity(
        string workflowKey,
        Guid instanceId,
        string transitionKey,
        string hook,
        string taskKey,
        string taskType,
        int attempt)
    {
        var activity = ActivitySource.StartActivity($"workflow.task {taskType}/{taskKey}", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("middt.workflow.key", workflowKey);
            activity.SetTag("middt.workflow.instance_id", instanceId.ToString());
            activity.SetTag("middt.workflow.transition", transitionKey);
            activity.SetTag("middt.workflow.hook", hook);
            activity.SetTag("middt.workflow.task.key", taskKey);
            activity.SetTag("middt.workflow.task.type", taskType);
            activity.SetTag("middt.workflow.task.attempt", attempt);
        }

        return activity;
    }

    internal static void CompleteTaskActivity(
        Activity? activity,
        string workflowKey,
        string taskType,
        string hook,
        string outcome,
        double durationMs,
        string? error = null)
    {
        if (activity is not null)
        {
            activity.SetTag("middt.workflow.task.outcome", outcome);
            if (!string.IsNullOrEmpty(error))
            {
                activity.SetStatus(ActivityStatusCode.Error, error);
            }
        }

        var tags = new TagList
        {
            { "workflow", workflowKey },
            { "task_type", taskType },
            { "hook", hook },
            { "outcome", outcome },
        };
        TasksExecuted.Add(1, tags);
        TaskDuration.Record(durationMs, tags);
    }
}
