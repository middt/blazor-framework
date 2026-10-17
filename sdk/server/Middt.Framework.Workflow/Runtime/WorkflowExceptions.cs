namespace Middt.Framework.Workflow.Runtime;

/// <summary>Base of errors that map to HTTP problem responses.</summary>
public abstract class WorkflowException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class WorkflowNotFoundException(string message) : WorkflowException(message, 404);

public sealed class WorkflowForbiddenException(string message) : WorkflowException(message, 403);

/// <summary>Instance busy, finished, waiting for a subflow, or transition not available in the current state.</summary>
public sealed class WorkflowConflictException(string message) : WorkflowException(message, 409);

public sealed class WorkflowValidationException(IReadOnlyList<string> errors)
    : WorkflowException("The request is not valid for this transition.", 400)
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Thrown by tasks for business or transport failures (retried/handled by the task's error policy).</summary>
public class WorkflowTaskException(string message, Exception? inner = null, int? statusCode = null) : Exception(message, inner)
{
    public int? StatusCode { get; init; } = statusCode;
}

/// <summary>Invalid workflow definitions found at startup.</summary>
public sealed class WorkflowDefinitionException(IReadOnlyList<string> errors)
    : Exception("Invalid workflow definition(s):" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => " - " + e)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
