using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Middt.Framework.Expressions;
using Middt.Framework.Workflow.Definitions;
using Middt.Framework.Workflow.Expressions;
using Middt.Framework.Workflow.Runtime;
using Middt.Framework.Workflow.Tasks;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Tasks;

/// <summary>
/// A business rule the user can fix (duplicate e-mail, not enough stock, store without staff …). The Turkish message is
/// written to <c>data.error</c> instead of faulting the instance, so the workflow can route to a correction step that
/// shows it (raw task errors are hidden from callers outside Development).
/// </summary>
public sealed class BikeRuleException(string message) : Exception(message);

/// <summary>
/// Base of the bike store workflow tasks: renders the config (<c>{{ expression }}</c> placeholders), resolves the scoped
/// <see cref="BikeStoresContext"/> of the engine and turns <see cref="BikeRuleException"/> into <c>data.error</c>.
/// <para>
/// Tasks run outside the engine's transaction and may be retried, so every write is idempotent: the created id (or an
/// operation marker) is stored in the instance data and a second run with the marker present does nothing. Keep such a
/// task the only write of its state so a later failing task cannot roll the marker back.
/// </para>
/// </summary>
public abstract partial class BikeTask : IWorkflowTask
{
    /// <summary>Data path of the user visible rule violation (removed when the task runs again).</summary>
    public const string ErrorPath = "error";

    /// <summary>Config keys that must be present.</summary>
    protected abstract IReadOnlyList<string> RequiredArguments { get; }

    public virtual IEnumerable<string> Validate(TaskDefinition task, WorkflowExpressionEngine expressions)
    {
        foreach (var name in RequiredArguments.Where(name => task.Config[name] is null))
        {
            yield return $"'{name}' is required.";
        }

        if (task.Config["into"] is { } into && !IsDataPath(into))
        {
            yield return "'into' must be a data path (letters, digits, dots).";
        }

        foreach (var error in expressions.ValidateTemplate(task.Config))
        {
            yield return error;
        }
    }

    public async Task ExecuteAsync(WorkflowTaskContext context, CancellationToken cancellationToken)
    {
        var args = new BikeTaskArguments(context.Expressions.Render(context.Config, context.Scope) as JsonObject ?? []);
        var db = context.Services.GetRequiredService<BikeStoresContext>();
        context.Data.Remove(ErrorPath);
        try
        {
            await RunAsync(context, args, db, cancellationToken);
        }
        catch (BikeRuleException ex)
        {
            db.ChangeTracker.Clear();
            context.Data[ErrorPath] = ex.Message;
        }
    }

    protected abstract Task RunAsync(WorkflowTaskContext context, BikeTaskArguments args, BikeStoresContext db, CancellationToken cancellationToken);

    /// <summary>Id stored by an earlier run at <paramref name="path"/> (the idempotency marker), or <c>null</c>.</summary>
    protected static int? StoredId(WorkflowTaskContext context, string path) =>
        JsonPath.Get(context.Data, path) is JsonValue value && value.GetValueKind() == JsonValueKind.Number &&
        int.TryParse(value.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

    protected static void SetData(WorkflowTaskContext context, string path, JsonNode? value) => JsonPath.Set(context.Data, path, value);

    protected static DateTime Today(WorkflowTaskContext context) =>
        (context.Services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime.Date;

    internal static bool IsDataPath(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && DataPathRegex().IsMatch(text);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}(\.[A-Za-z_][A-Za-z0-9_]{0,63}){0,7}$", RegexOptions.CultureInvariant)]
    private static partial Regex DataPathRegex();
}

/// <summary>The rendered task config with typed accessors (missing or malformed values are definition bugs → task failure).</summary>
public sealed class BikeTaskArguments(JsonObject values)
{
    public string? Text(string name) => values[name] switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>().Trim() is { Length: > 0 } text ? text : null,
        JsonValue v when v.GetValueKind() is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
        JsonValue v when v.GetValueKind() == JsonValueKind.Null => null,
        _ => throw new WorkflowTaskException($"'{name}' must be a text value."),
    };

    public string RequiredText(string name) => Text(name) ?? throw new WorkflowTaskException($"'{name}' is required.");

    public decimal? Number(string name)
    {
        switch (values[name])
        {
            case null:
                return null;
            case JsonValue v when v.GetValueKind() == JsonValueKind.Null:
                return null;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetValue<string>()):
                return null;
            case JsonValue v when v.GetValueKind() is JsonValueKind.Number or JsonValueKind.String &&
                decimal.TryParse(v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                return number;
            default:
                throw new WorkflowTaskException($"'{name}' must be a number.");
        }
    }

    public decimal RequiredNumber(string name) => Number(name) ?? throw new WorkflowTaskException($"'{name}' is required.");

    public int? WholeNumber(string name) => Number(name) switch
    {
        null => null,
        { } n when decimal.IsInteger(n) && n is >= int.MinValue and <= int.MaxValue => (int)n,
        _ => throw new WorkflowTaskException($"'{name}' must be an integer."),
    };

    public int RequiredInt(string name) => WholeNumber(name) ?? throw new WorkflowTaskException($"'{name}' is required.");

    public bool Bool(string name) => values[name] is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    /// <summary>The <c>into</c> data path, or <paramref name="fallback"/>.</summary>
    public string Into(string fallback) => Text("into") ?? fallback;
}
