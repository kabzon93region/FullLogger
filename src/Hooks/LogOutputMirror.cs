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
            var mode = PluginCore.Instance?.CaptureController?.GetLogOutputCaptureMode()
                ?? Logging.LogOutputCaptureMode.ErrorsAndKeywords;
            var bootstrapBytes = mode == Logging.LogOutputCaptureMode.All
                ? 1024 * 1024
                : 128 * 1024;

            BackgroundLogTailer.Instance.RegisterFile(
                _path,
                LogCategories.BepInExFile,
                bootstrapTailBytes: bootstrapBytes);

            SessionBootstrap.Write(LogCategories.BepInEx, "INFO",
                $"LogOutput mirror: background tail mode={mode} bootstrap={bootstrapBytes / 1024}KB ({_path})");
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
