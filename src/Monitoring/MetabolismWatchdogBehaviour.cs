using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using FullLogger.Logging;
using UnityEngine;

namespace FullLogger.Monitoring
{
    internal sealed class MetabolismWatchdogBehaviour : MonoBehaviour
    {
        private PluginCore _plugin;
        private float _timer;
        private float _stuckSeconds;
        private MetabolismSample _lastSample;
        private bool _hasLastSample;
        private int _resyncAttempts;

        internal void Initialize(PluginCore plugin)
        {
            _plugin = plugin;
        }

        private void Update()
        {
            if (_plugin == null
                || _plugin.CaptureController?.IsCaptureActive != true
                || !_plugin.MetabolismWatchdogEnabled.Value)
            {
                return;
            }

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f)
            {
                return;
            }

            _timer = Mathf.Max(1f, _plugin.MetabolismWatchIntervalSec.Value);
            Tick();
        }

        private void Tick()
        {
            if (!MetabolismSampler.TrySample(out var sample))
            {
                _stuckSeconds = 0f;
                _hasLastSample = false;
                return;
            }

            if (_plugin.MetabolismVerboseLogging.Value)
            {
                SessionBootstrap.Write(LogCategories.Metabolism, "INFO", sample.FormatLine());
            }

            if (!sample.PlayerAlive)
            {
                _stuckSeconds = 0f;
                _hasLastSample = false;
                return;
            }

            var frozen = sample.IsAtMax && sample.RatesNearZero;
            var unchanged = _hasLastSample
                && Mathf.Approximately(_lastSample.EnergyCurrent, sample.EnergyCurrent)
                && Mathf.Approximately(_lastSample.HydrationCurrent, sample.HydrationCurrent)
                && _lastSample.MetabolismDisabled == sample.MetabolismDisabled;

            if (frozen && unchanged)
            {
                _stuckSeconds += _plugin.MetabolismWatchIntervalSec.Value;
            }
            else
            {
                _stuckSeconds = 0f;
                _resyncAttempts = 0;
            }

            _lastSample = sample;
            _hasLastSample = true;

            var threshold = Mathf.Max(5f, _plugin.MetabolismStuckThresholdSec.Value);
            if (_stuckSeconds < threshold)
            {
                return;
            }

            var fikaNet = FikaNetworkStatsMonitor.TryRead();
            SessionBootstrap.Write(
                LogCategories.Metabolism,
                "WARN",
                $"[METABOLISM_STUCK] stuckFor={_stuckSeconds:0.0}s {sample.FormatLine()} {fikaNet.FormatLine()}");

            if (!_plugin.MetabolismAutoResync.Value)
            {
                return;
            }

            var maxAttempts = Mathf.Clamp(_plugin.MetabolismMaxResyncAttempts.Value, 1, 20);
            if (_resyncAttempts >= maxAttempts)
            {
                return;
            }

            _resyncAttempts++;
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            var health = player?.ActiveHealthController as ActiveHealthController;
            if (health == null)
            {
                return;
            }

            var ok = MetabolismResyncService.TryResync(health, out var action);
            SessionBootstrap.Write(
                LogCategories.Metabolism,
                ok ? "INFO" : "WARN",
                $"[METABOLISM_RESYNC] attempt={_resyncAttempts}/{maxAttempts} ok={ok} action={action}");
        }
    }
}
