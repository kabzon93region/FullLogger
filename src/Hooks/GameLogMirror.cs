using System;
using System.IO;
using System.Linq;
using FullLogger.Logging;
using UnityEngine;

namespace FullLogger.Hooks
{
    /// <summary>
    /// Registers BSG Logs/**/*.log with the background tailer.
    /// </summary>
    internal sealed class GameLogMirror : IDisposable
    {
        private FileSystemWatcher _watcher;
        private string _watchedDir;
        private bool _disposed;

        internal void Start()
        {
            var logsRoot = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            if (!Directory.Exists(logsRoot))
            {
                SessionBootstrap.Write(LogCategories.GameLog, "WARN", $"Game logs folder not found: {logsRoot}");
                return;
            }

            var latestSession = Directory.GetDirectories(logsRoot)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(latestSession))
            {
                return;
            }

            _watchedDir = latestSession;
            RegisterAllLogs(latestSession);

            _watcher = new FileSystemWatcher(latestSession)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            _watcher.Created += OnFsEvent;
            _watcher.Changed += OnFsEvent;
            _watcher.EnableRaisingEvents = true;

            SessionBootstrap.Write(LogCategories.GameLog, "INFO",
                $"Game log mirror: background tail watching {latestSession}");
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            if (e.FullPath.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                RegisterLogFile(e.FullPath);
            }
        }

        private void RegisterAllLogs(string dir)
        {
            foreach (var file in Directory.GetFiles(dir, "*.log", SearchOption.AllDirectories))
            {
                RegisterLogFile(file);
            }
        }

        private void RegisterLogFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            var rel = path;
            if (!string.IsNullOrEmpty(_watchedDir)
                && path.StartsWith(_watchedDir, StringComparison.OrdinalIgnoreCase))
            {
                rel = path.Substring(_watchedDir.Length).TrimStart(Path.DirectorySeparatorChar);
            }

            var relative = rel;
            BackgroundLogTailer.Instance.RegisterFile(
                path,
                LogCategories.GameLog,
                bootstrapTailBytes: 512 * 1024,
                decorateLine: line => $"[{relative}] {line}");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFsEvent;
                _watcher.Changed -= OnFsEvent;
                _watcher.Dispose();
            }
        }
    }
}
