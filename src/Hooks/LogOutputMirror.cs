using System;
using System.IO;
using BepInEx;
using FullLogger.Logging;

namespace FullLogger.Hooks
{
    /// <summary>
    /// Registers BepInEx/LogOutput.log with the background tailer (no main-thread poll).
    /// </summary>
    internal sealed class LogOutputMirror : IDisposable
    {
        private readonly string _path;
        private bool _disposed;

        internal LogOutputMirror()
        {
            _path = Path.Combine(Path.GetFullPath(Path.Combine(BepInEx.Paths.PluginPath, "..")), "LogOutput.log");
        }

        internal void Start()
        {
            BackgroundLogTailer.Instance.RegisterFile(
                _path,
                LogCategories.BepInExFile,
                bootstrapTailBytes: 1024 * 1024);

            SessionBootstrap.Write(LogCategories.BepInEx, "INFO",
                $"LogOutput mirror: background tail ({_path})");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                BackgroundLogTailer.Instance.UnregisterFile(_path);
            }
            catch
            {
                // tailer may already be disposed
            }
        }
    }
}
