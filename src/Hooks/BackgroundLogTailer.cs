using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using FullLogger.Logging;

namespace FullLogger.Hooks
{
    /// <summary>
    /// Reads log file tails on a background thread — no main-thread file I/O during gameplay.
    /// </summary>
    internal sealed class BackgroundLogTailer : IDisposable
    {
        private sealed class TailTarget
        {
            internal string Path;
            internal string Category;
            internal long Position;
            internal long BootstrapTailBytes;
            internal Func<string, string> DecorateLine;
        }

        private static BackgroundLogTailer _instance;

        private readonly object _sync = new object();
        private readonly List<TailTarget> _targets = new List<TailTarget>();
        private readonly int _pollIntervalMs;
        private Thread _worker;
        private volatile bool _disposed;

        internal static BackgroundLogTailer Instance =>
            _instance ?? throw new InvalidOperationException("BackgroundLogTailer not started");

        internal static BackgroundLogTailer Start(int pollIntervalMs)
        {
            if (_instance != null)
            {
                return _instance;
            }

            _instance = new BackgroundLogTailer(pollIntervalMs);
            _instance.StartWorker();
            return _instance;
        }

        private BackgroundLogTailer(int pollIntervalMs)
        {
            _pollIntervalMs = Math.Max(50, pollIntervalMs);
        }

        private void StartWorker()
        {
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "FullLogger-Tailer"
            };
            _worker.Start();
        }

        internal void RegisterFile(
            string path,
            string category,
            long bootstrapTailBytes = 1024 * 1024,
            Func<string, string> decorateLine = null)
        {
            if (string.IsNullOrEmpty(path) || _disposed)
            {
                return;
            }

            lock (_sync)
            {
                foreach (var existing in _targets)
                {
                    if (string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                _targets.Add(new TailTarget
                {
                    Path = path,
                    Category = category,
                    Position = 0,
                    BootstrapTailBytes = Math.Max(0, bootstrapTailBytes),
                    DecorateLine = decorateLine
                });
            }
        }

        internal void UnregisterFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            lock (_sync)
            {
                _targets.RemoveAll(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void WorkerLoop()
        {
            while (!_disposed)
            {
                TailTarget[] snapshot;
                lock (_sync)
                {
                    snapshot = _targets.ToArray();
                }

                foreach (var target in snapshot)
                {
                    try
                    {
                        TailFile(target);
                    }
                    catch
                    {
                        // retry next tick
                    }
                }

                Thread.Sleep(_pollIntervalMs);
            }
        }

        private void TailFile(TailTarget target)
        {
            if (!File.Exists(target.Path))
            {
                return;
            }

            using (var fs = new FileStream(target.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (target.Position == 0 && fs.Length > 0)
                {
                    var readFrom = Math.Max(0, fs.Length - target.BootstrapTailBytes);
                    if (readFrom > 0)
                    {
                        SessionBootstrap.Write(target.Category, "INFO",
                            $"Tail bootstrap {(fs.Length - readFrom) / 1024}KB from {target.Path}");
                    }

                    fs.Seek(readFrom, SeekOrigin.Begin);
                    target.Position = readFrom;
                }

                if (fs.Length < target.Position)
                {
                    target.Position = 0;
                }

                if (fs.Length <= target.Position)
                {
                    return;
                }

                fs.Seek(target.Position, SeekOrigin.Begin);
                using (var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        WriteLine(target, line);
                    }

                    target.Position = fs.Length;
                }
            }
        }

        private static void WriteLine(TailTarget target, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (target.DecorateLine != null)
            {
                line = target.DecorateLine(line);
            }

            var normalized = target.Category == LogCategories.BepInExFile
                ? LogCaptureCoordinator.NormalizeLogOutputLine(line)
                : line;

            if (!LogCaptureCoordinator.ShouldWrite(target.Category, "INFO", normalized))
            {
                return;
            }

            SessionBootstrap.Write(target.Category, "INFO", normalized);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_worker != null && _worker.IsAlive)
            {
                _worker.Join(TimeSpan.FromSeconds(3));
            }

            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }
    }
}
