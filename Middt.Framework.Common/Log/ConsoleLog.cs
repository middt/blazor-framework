using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;

namespace Middt.Framework.Common.Log
{
    public sealed class ConsoleLog : IBaseLog
    {


        public void Debug(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            Console.WriteLine(logMessage);
        }

        public void Error(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {

            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);
            Console.WriteLine(logMessage);
        }


        public void Fatal(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            Console.WriteLine(logMessage);
        }


        public void Info(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);
            Console.WriteLine(logMessage);
        }


        public void Warn(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            Console.WriteLine(logMessage);
        }


    }
}
