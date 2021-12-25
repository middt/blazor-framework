using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;

namespace Middt.Framework.Common.Log
{
    public sealed class Log4Net : IBaseLog
    {
        private readonly log4net.ILog log;

        public Log4Net()
        {
            string fileName = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            fileName += @"\config\log4net.config";
            XmlDocument log4netConfig = new XmlDocument();
            log4netConfig.Load(File.OpenRead(fileName));

            var repo = log4net.LogManager.CreateRepository(
                Assembly.GetEntryAssembly(), typeof(log4net.Repository.Hierarchy.Hierarchy));

            log4net.Config.XmlConfigurator.Configure(repo, log4netConfig["log4net"]);

            log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        }

        public void Debug(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            log.Debug(logMessage);
        }

        public void Error(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {

            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);
            log.Error(logMessage);
        }


        public void Fatal(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            log.Fatal(logMessage);
        }


        public void Info(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);
            log.Info(logMessage);
        }


        public void Warn(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0)
        {
            string logMessage = @"Class: {0} Method: {1} Satır: {2} Hata: {3}";
            logMessage = string.Format(logMessage, file, method, line, message);

            log.Warn(logMessage);
        }


    }
}
