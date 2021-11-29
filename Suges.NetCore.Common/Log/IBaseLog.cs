using System.Runtime.CompilerServices;

namespace Suges.Framework.Common.Log
{
    public interface IBaseLog
    {
        void Debug(object message,
   [CallerFilePath] string file = null,
   [CallerMemberName] string method = null,
   [CallerLineNumber] int line = 0);

        void Error(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0);

        void Fatal(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0);
        void Info(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0);

        void Warn(object message,
            [CallerFilePath] string file = null,
            [CallerMemberName] string method = null,
            [CallerLineNumber] int line = 0);
    }
}
