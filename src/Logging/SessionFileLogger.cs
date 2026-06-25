using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace FullLogger.Logging
{
    internal readonly struct LogEntry
    {
        internal readonly DateTime Timestamp;
        internal readonly string Category;
        internal readonly string Level;
        internal readonly string Message;
        internal readonly bool IsFlush;

        internal LogEntry(string category, string level, string message)
        {
            Timestamp = DateTime.Now;
            Category = category;
            Level = level;
            Message = message;
            IsFlush = false;
        }

        private LogEntry(bool flush)
        {
            Timestamp = default;
            Category = null;
            Level = null;
            Message = null;
            IsFlush = flush;
        }

        internal static LogEntry FlushMarker => new LogEntry(true);
    }

    internal sealed class SessionFileLogger : IDisposable
    {
        private static SessionFileLogger _instance;

        internal static SessionFileLogger Instance => _instance;

        private readonly object _fileSync = new object();
        private readonly string _sessionDir;
        private readonly long _maxPartBytes;
        private readonly bool _backgroundWrite;
        private readonly int _maxPending;
        private readonly BlockingCollection<LogEntry> _queue;
        private readonly Thread _worker;
        private StreamWriter _writer;
        private long _currentPartBytes;
        private int _partIndex;
        private volatile bool _disposed;
        private long _dropped;
        private long _lastDroppedReport;

        internal string SessionDirectory => _sessionDir;

        private SessionFileLogger(string sessionDir, long maxPartBytes, bool backgroundWrite, int maxPending)
        {
            _sessionDir = sessionDir;
            _maxPartBytes = maxPartBytes;
            _backgroundWrite = backgroundWrite;
            _maxPending = Math.Max(1024, maxPending);
            Directory.CreateDirectory(_sessionDir);

            if (_backgroundWrite)
            {
                _queue = new BlockingCollection<LogEntry>(_maxPending);
                _worker = new Thread(BackgroundWorkerLoop)
                {
                    IsBackground = true,
                    Name = "FullLogger-Writer"
                };
                _worker.Start();
            }
            else
            {
                OpenNextPartSync();
            }

            _instance = this;
        }

        internal static SessionFileLogger Create(string rootDir, int maxPartSizeMb, bool backgroundWrite, int maxPending)
        {
            var sessionName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var sessionDir = Path.Combine(rootDir, sessionName);
            var maxBytes = Math.Max(1, maxPartSizeMb) * 1024L * 1024L;
            return new SessionFileLogger(sessionDir, maxBytes, backgroundWrite, maxPending);
        }

        internal void Write(string category, string level, string message)
        {
            if (_disposed || string.IsNullOrEmpty(message))
            {
                return;
            }

            if (_backgroundWrite)
            {
                Enqueue(new LogEntry(category, level, message));
                return;
            }

            WriteLineSync(new LogEntry(category, level, message));
        }

        internal void Flush()
        {
            if (_disposed)
            {
                return;
            }

            if (_backgroundWrite)
            {
                Enqueue(LogEntry.FlushMarker);
                return;
            }

            lock (_fileSync)
            {
                _writer?.Flush();
            }
        }

        private void Enqueue(LogEntry entry)
        {
            if (_queue == null)
            {
                return;
            }

            if (_queue.TryAdd(entry))
            {
                return;
            }

            var dropped = Interlocked.Increment(ref _dropped);
            if (dropped - Volatile.Read(ref _lastDroppedReport) >= 1000)
            {
                Volatile.Write(ref _lastDroppedReport, dropped);
                _queue.TryAdd(new LogEntry(
                    LogCategories.Session,
                    "WARN",
                    $"Log queue full ({_maxPending} lines) — dropped {_dropped} entries"));
            }
        }

        private void BackgroundWorkerLoop()
        {
            try
            {
                OpenNextPartSync();

                foreach (var entry in _queue.GetConsumingEnumerable())
                {
                    if (entry.IsFlush)
                    {
                        lock (_fileSync)
                        {
                            _writer?.Flush();
                        }

                        continue;
                    }

                    WriteLineSync(entry);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var crashPath = Path.Combine(_sessionDir, "writer_crash.log");
                    File.WriteAllText(crashPath, ex.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // ignore
                }
            }
            finally
            {
                lock (_fileSync)
                {
                    try
                    {
                        _writer?.Flush();
                        _writer?.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }

                    _writer = null;
                }
            }
        }

        private void WriteLineSync(LogEntry entry)
        {
            var line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] [{entry.Category}] {entry.Message}";
            var bytes = Encoding.UTF8.GetByteCount(line) + 2;

            lock (_fileSync)
            {
                if (_writer == null)
                {
                    return;
                }

                _writer.WriteLine(line);
                _currentPartBytes += bytes;

                if (_currentPartBytes >= _maxPartBytes)
                {
                    RotatePartSync();
                }
            }
        }

        private void OpenNextPartSync()
        {
            _partIndex++;
            _currentPartBytes = 0;
            var path = Path.Combine(_sessionDir, $"part_{_partIndex:D5}.log");
            _writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = false };
            WriteLineSync(new LogEntry(LogCategories.Session, "INFO", $"Opened log part #{_partIndex}: {path}"));
        }

        private void RotatePartSync()
        {
            _writer.Flush();
            _writer.Dispose();
            OpenNextPartSync();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_backgroundWrite && _queue != null)
            {
                try
                {
                    Enqueue(new LogEntry(LogCategories.Session, "INFO", "Session logger shutting down"));
                    Enqueue(LogEntry.FlushMarker);
                    _disposed = true;
                    _queue.CompleteAdding();
                }
                catch
                {
                    _disposed = true;
                }

                if (_worker != null && _worker.IsAlive)
                {
                    _worker.Join(TimeSpan.FromSeconds(10));
                }

                _queue.Dispose();
            }
            else
            {
                _disposed = true;
                lock (_fileSync)
                {
                    try
                    {
                        WriteLineSync(new LogEntry(LogCategories.Session, "INFO", "Session logger shutting down"));
                        _writer?.Flush();
                        _writer?.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }

                    _writer = null;
                }
            }

            SessionSummaryWriter.WriteSummaryFile();

            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }
    }
}
