//using Microsoft.AppCenter;
//using Microsoft.AppCenter.Analytics;
//using Microsoft.AppCenter.Crashes;
//using Suges.Framework.Common.Configuration;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Reflection;
//using System.Runtime.CompilerServices;
//using System.Xml;

//namespace Suges.Framework.Common.Log
//{
//    public sealed class AppCenterLog : IBaseLog
//    {

//        public AppCenterLog(IBaseConfiguration baseConfiguration)
//        {
//            AppCenter.Start("c5b86b94-e83e-4885-bae1-4ece1df0c202",
//                      typeof(Analytics), typeof(Crashes));
//        }

//        public void Debug(object message,
//            [CallerFilePath] string file = null,
//            [CallerMemberName] string method = null,
//            [CallerLineNumber] int line = 0)
//        {
//            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
//            logMessage = string.Format(logMessage, file, method, line, message);


//            var properties = new Dictionary<string, string>
//    {
//        { "file", file},
//        { "method", method},
//        { "line", line.ToString()},
//        { "message", message.ToString()}
//    };


//            Analytics.TrackEvent("Debug", properties);
//        }

//        public void Error(object message,
//            [CallerFilePath] string file = null,
//            [CallerMemberName] string method = null,
//            [CallerLineNumber] int line = 0)
//        {

//            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
//            logMessage = string.Format(logMessage, file, method, line, message);


//            var properties = new Dictionary<string, string>
//    {
//        { "file", file},
//        { "method", method},
//        { "line", line.ToString()},
//        { "message", message.ToString()}
//    };

//            Crashes.TrackError(new Exception(logMessage), properties);
//        }


//        public void Fatal(object message,
//            [CallerFilePath] string file = null,
//            [CallerMemberName] string method = null,
//            [CallerLineNumber] int line = 0)
//        {
//            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
//            logMessage = string.Format(logMessage, file, method, line, message);


//            var properties = new Dictionary<string, string>
//    {
//        { "file", file},
//        { "method", method},
//        { "line", line.ToString()},
//        { "message", message.ToString()}
//    };

//            Crashes.TrackError(new Exception(logMessage), properties);
//        }


//        public void Info(object message,
//            [CallerFilePath] string file = null,
//            [CallerMemberName] string method = null,
//            [CallerLineNumber] int line = 0)
//        {
//            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
//            logMessage = string.Format(logMessage, file, method, line, message);


//            var properties = new Dictionary<string, string>
//    {
//        { "file", file},
//        { "method", method},
//        { "line", line.ToString()},
//        { "message", message.ToString()}
//    };


//            Analytics.TrackEvent("Info", properties);
//        }


//        public void Warn(object message,
//            [CallerFilePath] string file = null,
//            [CallerMemberName] string method = null,
//            [CallerLineNumber] int line = 0)
//        {
//            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
//            logMessage = string.Format(logMessage, file, method, line, message);


//            var properties = new Dictionary<string, string>
//    {
//        { "file", file},
//        { "method", method},
//        { "line", line.ToString()},
//        { "message", message.ToString()}
//    };


//            Analytics.TrackEvent("Warn", properties);
//        }


//    }
//}
