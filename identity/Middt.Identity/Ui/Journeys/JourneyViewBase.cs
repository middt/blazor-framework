using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;

namespace Middt.Identity.Ui.Journeys;

/// <summary>
/// Base class of journey views. A view renders the current step of <see cref="State"/> and posts action keys with
/// <see cref="Components.Shared.JourneyForm"/>; field names are the property names of the action's input schema.
/// Views are rendered with static server rendering: no event handlers besides the form posts.
/// </summary>
public abstract class JourneyViewBase : ComponentBase
{
    [Parameter, EditorRequired]
    public AuthJourneyState State { get; set; } = default!;

    /// <summary>Non-sensitive values of the last (failed) post, to fill the form again.</summary>
    [Parameter]
    public IReadOnlyDictionary<string, string> Values { get; set; } = new Dictionary<string, string>();

    /// <summary>Errors to show (journey errors of the last submit and UI checks); render with <c>ErrorSummary</c>.</summary>
    [Parameter]
    public IReadOnlyList<string> Errors { get; set; } = [];

    protected bool Has(string action) => State.Actions.Any(a => string.Equals(a.Key, action, StringComparison.Ordinal));

    protected AuthJourneyAction? Action(string action) => State.Actions.FirstOrDefault(a => string.Equals(a.Key, action, StringComparison.Ordinal));

    /// <summary>Title of <paramref name="action"/> from the journey definition, or <paramref name="fallback"/>.</summary>
    protected string Title(string action, string fallback) => Action(action)?.Title is { Length: > 0 } title ? title : fallback;

    /// <summary>String value of <c>data.{key}</c> (UI-safe data only).</summary>
    protected string? Data(string key) => State.Data[key] switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        JsonValue value => value.ToJsonString(),
        _ => null,
    };

    /// <summary>String items of the array <c>data.{key}</c> (a space separated string is split as well, e.g. scopes).</summary>
    protected IReadOnlyList<string> DataList(string key) => State.Data[key] switch
    {
        JsonArray array => [.. array.Select(n => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : n?.ToJsonString()).OfType<string>()],
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>().Split(' ', StringSplitOptions.RemoveEmptyEntries),
        _ => [],
    };

    /// <summary>Posted value of <paramref name="field"/> (after a failed submit), else <c>data.{field}</c>.</summary>
    protected string? Value(string field) => Values.TryGetValue(field, out var value) ? value : Data(field);

    /// <summary>Back to the sign-in page keeping the return URL.</summary>
    protected string LoginUrl => JourneyLinks.WithReturnUrl(MiddtIdentityDefaults.LoginPath, State.ReturnUrl);

    protected string RegisterUrl => JourneyLinks.WithReturnUrl(MiddtIdentityDefaults.RegisterPath, State.ReturnUrl);

    protected string ForgotPasswordUrl => JourneyLinks.WithReturnUrl(MiddtIdentityDefaults.ForgotPasswordPath, State.ReturnUrl);
}

/// <summary>
/// Cascaded by the journey page: <see cref="Components.Shared.JourneyForm"/> posts dispatch to it. The callback's receiver
/// is the page, so the page (not only the form) re-renders after a submit (e.g. to show errors).
/// </summary>
public sealed class JourneyFormContext(EventCallback submit)
{
    public Task SubmitAsync() => submit.InvokeAsync();
}
