using System;
using System.IO;
using System.Text;

namespace Nivan.Shield.Services
{
    public sealed class LogLineEventArgs : EventArgs
    {
        public LogLineEventArgs(string line) { Line = line; }
        public string Line { get; private set; }
    }

    public sealed class AppLogger
    {
        private readonly string _path;
        private readonly object _sync = new object();

        public AppLogger(string path)
        {
            _path = path;
        }

        public event EventHandler<LogLineEventArgs> LineWritten;

        public string Path { get { return _path; } }

        public void Info(string message) { Write("INFO", message); }
        public void Warning(string message) { Write("WARNING", message); }
        public void Error(string message) { Write("ERROR", message); }
        public void Connected(string message) { Write("CONNECTED", message); }

        public void Write(string level, string message)
        {
            string safeMessage = (message ?? String.Empty).Replace("\r", " ").Replace("\n", " | ");
            string line = String.Format(
                "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}",
                DateTime.Now,
                level,
                safeMessage
            );

            try
            {
                lock (_sync)
                {
                    File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never interrupt a connection attempt.
            }

            EventHandler<LogLineEventArgs> handler = LineWritten;
            if (handler != null) handler(this, new LogLineEventArgs(line));
        }

        public string ReadTail(int maximumLines)
        {
            try
            {
                if (!File.Exists(_path)) return "No activity has been recorded yet.";
                string[] lines;
                lock (_sync) lines = File.ReadAllLines(_path, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - Math.Max(1, maximumLines));
                return String.Join(Environment.NewLine, lines, start, lines.Length - start);
            }
            catch
            {
                return "The activity log could not be read.";
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                File.WriteAllText(_path, String.Empty, new UTF8Encoding(false));
            }
            Info("Activity log cleared.");
        }
    }
}
