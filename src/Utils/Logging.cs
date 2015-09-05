using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Degausser.Utils
{
    static class Logging
    {
        public static event EventHandler<string> OnMessage;

        public static void Log(Exception e)
        {
            Log(e.Message);
        }

        public static void Log(string message)
        {
            var timeStampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Trace.WriteLine(timeStampedMessage);
            if (OnMessage != null)
            {
                OnMessage(null, timeStampedMessage);
            }
        }

        public static void AssertEqual<T>(T item1, T item2, string message)
        {
            Debug.Assert(item1.Equals(item2), message);
            if (!item1.Equals(item2))
            {
                throw new InvalidOperationException(message);
            }
        }

        public static IEnumerable<T> SkipExceptions<T>(this IEnumerable<T> source, bool log)
        {
            using (var enumerator = source.GetEnumerator())
            {
                bool next = true;
                while (next)
                {
                    try
                    {
                        next = enumerator.MoveNext();
                    }
                    catch (Exception e)
                    {
                        if (log) Log(e);
                        continue;
                    }
                    if (next) yield return enumerator.Current;
                }
            }
        }
    }
}
