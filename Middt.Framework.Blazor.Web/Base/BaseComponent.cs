using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Middt.Framework.Blazor.Web.Base.Component.Modal;
using Middt.Framework.Common.Notification;
using Middt.Framework.Common.Security;

namespace Middt.Framework.Blazor.Web.Base;

/// <summary>
/// Base for all framework components: injects common services, loads the current user's claims on first
/// render and offers <see cref="ExecuteMethod"/> to run work behind the loading indicator with uniform error handling.
/// </summary>
public abstract class BaseComponent : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource disposeCts = new();
    private ILogger? logger;

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    protected INotification Notification { get; set; } = default!;

    [Inject]
    protected ILoggerFactory LoggerFactory { get; set; } = default!;

    [Inject]
    protected IJSRuntime JsRuntime { get; set; } = default!;

    [Inject]
    public BaseTokenHelper BaseTokenHelper { get; set; } = default!;

    protected ILogger Logger => logger ??= LoggerFactory.CreateLogger(GetType());

    /// <summary>Cancelled when the component is disposed (e.g. the user navigates away).</summary>
    protected CancellationToken ComponentCancellation => disposeCts.Token;

    public BaseLoadingModal? LoadingModal { get; set; }

    public bool IsAuthenticated { get; protected set; }

    /// <summary><c>true</c> once the user's claims are loaded (already during prerendering with cookie authentication).</summary>
    public bool IsUserLoaded { get; private set; }

    /// <summary><c>true</c> after the first interactive render; data loading starts from here.</summary>
    protected bool IsFirstRenderCompleted { get; private set; }

    /// <summary>
    /// Runs <paramref name="action"/> while the loading indicator is shown. Exceptions are logged and
    /// surfaced as a generic notification instead of tearing down the circuit.
    /// </summary>
    public virtual async Task ExecuteMethod(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await OpenModal();
            await action();
        }
        catch (OperationCanceledException) when (disposeCts.IsCancellationRequested)
        {
            // component went away; nothing to report
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unhandled error in {Component}", GetType().Name);
            Notification.ShowErrorMessage("İşlem sırasında bir hata oluştu.", "Lütfen tekrar deneyin.");
        }
        finally
        {
            await CloseModal();
            await InvokeAsync(StateHasChanged);
        }
    }

    protected virtual Task OpenModal() => LoadingModal is { IsModalOpen: false } modal ? modal.Open() : Task.CompletedTask;

    protected virtual Task CloseModal() => LoadingModal?.Close() ?? Task.CompletedTask;

    /// <remarks>Derived components overriding this method must call <c>base.OnInitializedAsync()</c>.</remarks>
    protected override async Task OnInitializedAsync()
    {
        // The auth cookie is available from the first (prerendered) request, so claims are known before rendering.
        await BaseTokenHelper.LoadAsync();
        IsUserLoaded = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            IsFirstRenderCompleted = true;
            Notification.Clear();
            await CustomOnAfterRenderAsync(true);
            StateHasChanged();
        }
        else
        {
            await CustomOnAfterRenderAsync(false);
        }
    }

    /// <summary>Lifecycle hook executed after the user's claims have been loaded.</summary>
    protected virtual Task CustomOnAfterRenderAsync(bool firstRender) => Task.CompletedTask;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !disposeCts.IsCancellationRequested)
        {
            disposeCts.Cancel();
            disposeCts.Dispose();
        }
    }
}
