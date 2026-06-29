using System;
using System.IO;
using System.Linq;
using BepInEx;
using FullLogger.Logging;
using UnityEngine;

namespace FullLogger.Hooks
{
    /// <summary>
    /// Registers BSG Logs/**/*.log with the background tailer. Retries when folder appears after raid load.
    /// </summary>
    internal sealed class GameLogMirror : IDisposable
    {
        private FileSystemWatcher _rootWatcher;
        private FileSystemWatcher _sessionWatcher;
        private string _logsRoot;
        private string _watchedDir;
        private bool _disposed;

        internal bool IsAttached => !string.IsNullOrEmpty(_watchedDir);

        internal void Start()
        {
            _logsRoot = ResolveLogsRoot();
            TryAttachLatestSession(forceLog: true);

            if (string.IsNullOrEmpty(_logsRoot) || !Directory.Exists(_logsRoot))
            {
                SessionBootstrap.Write(LogCategories.GameLog, "WARN",
                    $"Game logs root not found yet ({_logsRoot ?? "null"}) — will retry during raid.");
                TryWatchLogsRoot();
                return;
            }

            TryWatchLogsRoot();
        }

        internal bool TryAttachLatestSession(bool forceLog)
        {
            if (_disposed)
            {
                return false;
            }

            _logsRoot ??= ResolveLogsRoot();
            if (string.IsNullOrEmpty(_logsRoot) || !Directory.Exists(_logsRoot))
            {
                return false;
            }

            var latestSession = Directory.GetDirectories(_logsRoot)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(latestSession))
            {
                return false;
            }

            if (string.Equals(_watchedDir, latestSession, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            DetachSessionWatcher();
            _watchedDir = latestSession;
            RegisterAllLogs(latestSession);

            _sessionWatcher = new FileSystemWatcher(latestSession)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            _sessionWatcher.Created += OnFsEvent;
            _sessionWatcher.Changed += OnFsEvent;
            _sessionWatcher.EnableRaisingEvents = true;

            if (forceLog)
            {
                SessionBootstrap.Write(LogCategories.GameLog, "INFO",
                    $"Game log mirror attached: {latestSession}");
            }

            return true;
        }

        private void TryWatchLogsRoot()
        {
            if (string.IsNullOrEmpty(_logsRoot) || !Directory.Exists(_logsRoot) || _rootWatcher != null)
            {
                return;
            }

            _rootWatcher = new FileSystemWatcher(_logsRoot)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };
            _rootWatcher.Created += OnRootDirCreated;
            _rootWatcher.EnableRaisingEvents = true;
        }

        private void OnRootDirCreated(object sender, FileSystemEventArgs e)
        {
            if (Directory.Exists(e.FullPath))
            {
                TryAttachLatestSession(forceLog: true);
            }
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

        private static string ResolveLogsRoot()
        {
            var candidates = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "Logs"),
                Path.Combine(Application.dataPath, "..", "Logs"),
                Path.Combine(BepInEx.Paths.GameRootPath ?? Directory.GetCurrentDirectory(), "Logs")
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(candidate);
                    if (Directory.Exists(full))
                    {
                        return full;
                    }
                }
                catch
                {
                    // try next
                }
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "Logs");
        }

        private void DetachSessionWatcher()
        {
            if (_sessionWatcher == null)
            {
                return;
            }

            _sessionWatcher.EnableRaisingEvents = false;
            _sessionWatcher.Created -= OnFsEvent;
            _sessionWatcher.Changed -= OnFsEvent;
            _sessionWatcher.Dispose();
            _sessionWatcher = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DetachSessionWatcher();

            if (_rootWatcher != null)
            {
                _rootWatcher.EnableRaisingEvents = false;
                _rootWatcher.Created -= OnRootDirCreated;
                _rootWatcher.Dispose();
                _rootWatcher = null;
            }
        }
    }
}
