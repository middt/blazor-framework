namespace Suges.Framework.Blazor.Web.Helper
{
    public sealed class NotificationHelper
    {
        private static readonly Lazy<NotificationHelper> LazyInstance = new Lazy<NotificationHelper>(() => new NotificationHelper());
        public static NotificationHelper Instance
        {
            get { return LazyInstance.Value; }
        }



    }
}
