using Middt.Framework.Common.Notification;
using Radzen;

namespace Middt.Framework.Blazor.Web.Base.Notification;

/// <summary><see cref="INotification"/> on top of Radzen's <see cref="NotificationService"/>.</summary>
public sealed class RadzenNotification(NotificationService notificationService) : INotification
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    public void ShowErrorMessage(string title, string? message) =>
        Show(NotificationSeverity.Error, title, message, sticky: true);

    public void ShowInfoMessage(string title, string? message) => Show(NotificationSeverity.Info, title, message);

    public void ShowSuccessMessage(string title, string? message) => Show(NotificationSeverity.Success, title, message);

    public void ShowWarningMessage(string title, string? message) => Show(NotificationSeverity.Warning, title, message);

    public void Clear() => notificationService.Messages.Clear();

    private void Show(NotificationSeverity severity, string title, string? message, bool sticky = false) =>
        notificationService.Notify(new NotificationMessage
        {
            Severity = severity,
            Summary = title,
            Detail = message ?? string.Empty,
            Duration = sticky ? null : DefaultDuration.TotalMilliseconds,
            CloseOnClick = true,
        });
}
