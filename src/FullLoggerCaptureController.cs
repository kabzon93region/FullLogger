using System;
using System.Text;
using BepInEx.Logging;
using FullLogger.Hooks;
using FullLogger.Logging;
using FullLogger.Monitoring;
using FullLogger.Tracing;
using HarmonyLib;
using UnityEngine;

namespace FullLogger
{
    /// <summary>
    /// Applies config at runtime (BepInEx SettingChanged) and supports full shutdown without restart.
    /// </summary>
    internal sealed class FullLoggerCaptureController
    {
        private readonly PluginCore _plugin;
        private readonly ManualLogSource _logger;
        private bool _initialized;
        private bool _shutdown;

        internal FullLoggerCaptureController(PluginCore plugin, ManualLogSource logger)
        {
            _plugin = plugin;
            _logger = logger;
        }

        internal void BindSettingChangedEvents()
        {
            _plugin.Enabled.SettingChanged += OnSettingChanged;
            _plugin.MirrorLogOutputFile.SettingChanged += OnSettingChanged;
            _plugin.MirrorGameLogs.SettingChanged += OnSettingChanged;
            _plugin.LogOutputCaptureMode.SettingChanged += OnSettingChanged;
            _plugin.TailPollIntervalMs.SettingChanged += OnSettingChanged;
            _plugin.TraceInventoryOps.SettingChanged += OnSettingChanged;
            _plugin.TraceCombatRicochet.SettingChanged += OnSettingChanged;
            _plugin.RaidMonitoringEnabled.SettingChanged += OnSettingChanged;
            _plugin.MetabolismWatchdogEnabled.SettingChanged += OnSettingChanged;
            _plugin.RuntimePollIntervalSeconds.SettingChanged += OnSettingChanged;
        }

        internal void MarkInitialized()
        {
            _initialized = true;
            LogActiveProfile("startup");
        }

        internal bool IsCaptureActive =>
            _initialized && !_shutdown && _plugin.Enabled.Value;

        internal bool ShouldTraceInventory =>
            IsCaptureActive && _plugin.TraceInventoryOps.Value;

        internal bool ShouldTraceRicochet =>
            IsCaptureActive && _plugin.TraceCombatRicochet.Value;

        internal LogOutputCaptureMode GetLogOutputCaptureMode()
        {
            if (!IsCaptureActive || !_plugin.MirrorLogOutputFile.Value)
            {
                return Logging.LogOutputCaptureMode.Off;
            }

            var raw = _plugin.LogOutputCaptureMode.Value;
            if (raw < 0 || raw > 3)
            {
                raw = (int)Logging.LogOutputCaptureMode.ErrorsAndKeywords;
            }

            return (Logging.LogOutputCaptureMode)raw;
        }

        internal void ApplyFromConfig(string reason)
        {
            if (!_initialized)
            {
                return;
            }

            if (!_plugin.Enabled.Value)
            {
                ShutdownCapture(reason);
                return;
            }

            if (_shutdown)
            {
                _logger.LogWarning(
                    "[FULL_LOGGER] Enabled=true but capture was shutdown — restart game to re-enable Full Logger.");
                return;
            }

            ApplyMirrorSettings();
            ApplyTailInterval();
            LogActiveProfile(reason);
        }

        private void OnSettingChanged(object sender, EventArgs e)
        {
            ApplyFromConfig("config-changed");
        }

        private void ApplyMirrorSettings()
        {
            if (_plugin.MirrorLogOutputFile.Value)
            {
                _plugin.EnsureLogOutputMirror();
            }
            else
            {
                _plugin.StopLogOutputMirror();
            }

            if (_plugin.MirrorGameLogs.Value)
            {
                _plugin.EnsureGameLogMirror();
            }
            else
            {
                _plugin.StopGameLogMirror();
            }
        }

        private void ApplyTailInterval()
        {
            if (_plugin.BackgroundTailer != null)
            {
                _plugin.BackgroundTailer.SetPollIntervalMs(_plugin.TailPollIntervalMs.Value);
            }
        }

        private void ShutdownCapture(string reason)
        {
            if (_shutdown)
            {
                return;
            }

            _shutdown = true;
            _plugin.StopLogOutputMirror();
            _plugin.StopGameLogMirror();
            _plugin.BackgroundTailer?.Pause(true);

            if (_plugin.MonitoringRoot != null)
            {
                _plugin.MonitoringRoot.SetActive(false);
            }

            _logger.LogWarning($"[FULL_LOGGER] Capture STOPPED ({reason}). Restart game to fully unload Harmony patches.");
            SessionBootstrap.Write(LogCategories.Session, "WARN", $"Capture stopped ({reason})");
        }

        private void LogActiveProfile(string reason)
        {
            var sb = new StringBuilder();
            sb.Append("[FULL_LOGGER] Active profile (").Append(reason).Append("): ");
            sb.Append("logOutput=").Append(_plugin.MirrorLogOutputFile.Value)
                .Append('/').Append(_plugin.LogOutputCaptureMode.Value);
            sb.Append(" gameLogs=").Append(_plugin.MirrorGameLogs.Value);
            sb.Append(" tailMs=").Append(_plugin.TailPollIntervalMs.Value);
            sb.Append(" raid=").Append(_plugin.RaidMonitoringEnabled.Value)
                .Append('/').Append(_plugin.RaidSnapshotEnabled.Value)
                .Append('@').Append(_plugin.RaidSnapshotIntervalSec.Value).Append('s');
            sb.Append(" metabolism=").Append(_plugin.MetabolismWatchdogEnabled.Value);
            sb.Append(" invTrace=").Append(_plugin.TraceInventoryOps.Value);
            sb.Append(" ricochet=").Append(_plugin.TraceCombatRicochet.Value);
            sb.Append(" extractTrace=").Append(_plugin.TraceExtractEvents.Value);
            sb.Append(" | NOTE: Harmony patches need restart; toggles above apply live.");
            _logger.LogInfo(sb.ToString());
        }
    }
}
