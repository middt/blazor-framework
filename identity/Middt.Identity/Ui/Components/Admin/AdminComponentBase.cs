using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radzen;

namespace Middt.Identity.Ui.Components.Admin;

/// <summary>
/// Base of the administration pages and dialogs (interactive server). Every operation runs in its own DI scope: a
/// circuit lives long and EF Core contexts (Identity, OpenIddict, workflow) must not be shared between operations.
/// </summary>
public abstract partial class AdminComponentBase : ComponentBase
{
    [Inject]
    protected IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject]
    protected NotificationService Notifications { get; set; } = default!;

    [Inject]
    protected DialogService Dialogs { get; set; } = default!;

    [Inject]
    private ILogger<AdminComponentBase> Logger { get; set; } = default!;

    /// <summary>Runs <paramref name="work"/> with the services of a new scope.</summary>
    protected async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = ScopeFactory.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>
    /// Runs a command in a new scope and notifies the result: the returned messages are shown as errors (validation, e.g.
    /// <see cref="IdentityResult"/> descriptions); exceptions are logged and shown as a generic message.
    /// </summary>
    protected async Task<bool> RunAsync(Func<IServiceProvider, Task<IReadOnlyList<string>>> work, string? success)
    {
        try
        {
            var errors = await WithScopeAsync(work);
            if (errors.Count > 0)
            {
                Notifications.Notify(NotificationSeverity.Error, "İşlem yapılamadı", string.Join(" ", errors), 8000);
                return false;
            }

            if (success is not null)
            {
                Notifications.Notify(NotificationSeverity.Success, success);
            }

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCommandFailed(Logger, exception);
            Notifications.Notify(NotificationSeverity.Error, "İşlem sırasında bir hata oluştu", "Ayrıntılar sistem kayıtlarındadır.", 8000);
            return false;
        }
    }

    /// <summary>Messages of a failed <see cref="IdentityResult"/> (empty on success).</summary>
    protected static IReadOnlyList<string> Errors(IdentityResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Succeeded ? [] : [.. result.Errors.Select(e => e.Description)];
    }

    protected static IReadOnlyList<string> Ok { get; } = [];

    [LoggerMessage(Level = LogLevel.Error, Message = "Identity administration command failed")]
    private static partial void LogCommandFailed(ILogger logger, Exception exception);
}
