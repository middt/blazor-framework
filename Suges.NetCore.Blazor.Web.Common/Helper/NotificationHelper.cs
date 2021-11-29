using Radzen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
