using Microsoft.JSInterop;
using Suges.Framework.Common.Dependency;
using Suges.Framework.Common.Log;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Suges.Framework.Blazor.Web.Log
{
    public class BrowserConsoleLog : IBaseLog
    {
        protected IJSRuntime JSRuntime { get; set; }
        public BrowserConsoleLog(IJSRuntime _jsRuntime)
        {
            JSRuntime = _jsRuntime;
        }

        public async void Debug(object message, [CallerFilePath] string file = null, [CallerMemberName] string method = null, [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            await JSRuntime.InvokeVoidAsync("LogThis", logMessage);
        }

        public async void Error(object message, [CallerFilePath] string file = null, [CallerMemberName] string method = null, [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            await JSRuntime.InvokeVoidAsync("LogThis", logMessage);
        }

        public async void Fatal(object message, [CallerFilePath] string file = null, [CallerMemberName] string method = null, [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            await JSRuntime.InvokeVoidAsync("LogThis", logMessage);
        }

        public async void Info(object message, [CallerFilePath] string file = null, [CallerMemberName] string method = null, [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            await JSRuntime.InvokeVoidAsync("LogThis", logMessage);
        }

        public async void Warn(object message, [CallerFilePath] string file = null, [CallerMemberName] string method = null, [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            await JSRuntime.InvokeVoidAsync("console.log", logMessage);
        }
    }
}
