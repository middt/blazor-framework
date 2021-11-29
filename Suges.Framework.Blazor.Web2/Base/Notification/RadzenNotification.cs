using Radzen;
using Suges.Framework.Common.Notification;

namespace Suges.Framework.Blazor.Web.Base.Notification
{
    public class RadzenNotification : INotification
    {
        NotificationService notificationService;
        public RadzenNotification(NotificationService _notificationService)
        {
            notificationService = _notificationService;
        }
        protected void ShowMessage(NotificationSeverity notificationSeverity, string title, string message)
        {
            NotificationMessage notificationMessage = new NotificationMessage();
           
            notificationMessage.Severity = notificationSeverity;
            notificationMessage.Summary = title;
            notificationMessage.Detail = message;
            notificationMessage.Duration = Int32.MaxValue;

            notificationService.Messages.Insert(0,notificationMessage);
        }

        public void ShowErrorMessage(string title, string message)
        {
            ShowMessage(NotificationSeverity.Error, title, message);
        }
        public void ShowInfoMessage(string title, string message)
        {
            ShowMessage(NotificationSeverity.Info, title, message);
        }

        public void ShowSuccessMessage(string title, string message)
        {
            ShowMessage(NotificationSeverity.Success, title, message);
        }
        public void ShowWarningMessage(string title, string message)
        {
            ShowMessage(NotificationSeverity.Warning, title, message);
        }
    }
}
