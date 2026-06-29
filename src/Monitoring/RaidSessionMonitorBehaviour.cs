using FullLogger.Hooks;
using FullLogger.Logging;
using UnityEngine;

namespace FullLogger.Monitoring
{
    /// <summary>
    /// Lightweight in-raid snapshots (health, Fika net, FPS). Background file tail handles LogOutput/BSG logs.
    /// </summary>
    internal sealed class RaidSessionMonitorBehaviour : MonoBehaviour
    {
        private PluginCore _plugin;
        private float _snapshotTimer;
        private float _gameLogRetryTimer;
        private bool _wasInRaid;
        private int _snapshotIndex;

        internal void Initialize(PluginCore plugin)
        {
            _plugin = plugin;
        }

        private void Update()
        {
            if (_plugin == null
                || _plugin.CaptureController?.IsCaptureActive != true
                || !_plugin.RaidMonitoringEnabled.Value)
            {
                return;
            }

            var delta = Time.unscaledDeltaTime;
            TickGameLogRetry(delta);
            TickRaidMonitoring(delta);
        }

        private void TickGameLogRetry(float delta)
        {
            if (_plugin.GameLogMirror == null)
            {
                return;
            }

            _gameLogRetryTimer -= delta;
            if (_gameLogRetryTimer > 0f)
            {
                return;
            }

            _gameLogRetryTimer = Mathf.Max(5f, _plugin.GameLogRetryIntervalSec.Value);
            _plugin.GameLogMirror.TryAttachLatestSession(forceLog: false);
        }

        private void TickRaidMonitoring(float delta)
        {
            var inRaid = RaidStateDetector.IsInRaidQuick();

            if (inRaid && !_wasInRaid)
            {
                _wasInRaid = true;
                _snapshotIndex = 0;
                SessionBootstrap.Write(LogCategories.Raid, "INFO", "[RAID_START] player entered raid world");
                _plugin.GameLogMirror?.TryAttachLatestSession(forceLog: true);
                FikaNetworkStatsMonitor.WritePeriodicIfInRaid(force: true);
            }
            else if (!inRaid && _wasInRaid)
            {
                _wasInRaid = false;
                SessionBootstrap.Write(LogCategories.Raid, "INFO", "[RAID_END] left raid world");
            }

            if (!inRaid || !_plugin.RaidSnapshotEnabled.Value)
            {
                return;
            }

            _snapshotTimer -= delta;
            if (_snapshotTimer > 0f)
            {
                return;
            }

            _snapshotTimer = Mathf.Max(3f, _plugin.RaidSnapshotIntervalSec.Value);
            WriteRaidSnapshot(delta);
        }

        private void WriteRaidSnapshot(float delta)
        {
            if (!RaidStateDetector.TryCapture(delta, out var raid))
            {
                return;
            }

            _snapshotIndex++;
            var healthLine = string.Empty;
            if (_plugin.RaidIncludeHealth.Value && MetabolismSampler.TrySample(out var metabolism))
            {
                healthLine = metabolism.FormatLine();
            }

            var fikaLine = FikaNetworkStatsMonitor.TryRead().FormatLine();
            SessionBootstrap.Write(
                LogCategories.Raid,
                "INFO",
                $"[RAID_SNAPSHOT #{_snapshotIndex}] {raid.FormatLine(healthLine, fikaLine)}");

            if (_plugin.FikaNetLogDuringRaid.Value)
            {
                FikaNetworkStatsMonitor.WritePeriodicIfInRaid(force: true);
            }
        }
    }
}
