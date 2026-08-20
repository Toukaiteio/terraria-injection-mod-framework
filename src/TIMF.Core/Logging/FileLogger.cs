using System;
using System.IO;
using System.Text;
using TIMF.Abstractions;

namespace TIMF.Core.Logging
{
    internal sealed class FileLogger : ILogger
    {
        private readonly object _gate = new object();
        private readonly string _path;
        private readonly string _prefix;

        public FileLogger(string path, string prefix = "TIMF")
        {
            _path = path;
            _prefix = prefix;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Debug(string message) => Write("DEBUG", message);

        public void Error(string message) => Write("ERROR", message);

        public void Error(string message, Exception exception)
        {
            // Exception messages can echo absolute filenames. Keep diagnostics useful without
            // copying local machine paths into framework logs.
            var detail = exception == null ? "Unknown error" : exception.GetType().Name;
            Write("ERROR", message + " (" + detail + ")");
        }

        private void Write(string level, string message)
        {
            var line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [{2}] {3}",
                DateTime.Now, level, _prefix, message);
            lock (_gate)
            {
                try
                {
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // never throw from logger
                }
            }
        }
    }
}
