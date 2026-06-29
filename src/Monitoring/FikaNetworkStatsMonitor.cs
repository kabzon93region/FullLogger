using System;
using System.Reflection;
using FullLogger.Logging;

namespace FullLogger.Monitoring
{
    internal struct FikaNetworkStatsSnapshot
    {
        internal bool Available;
        internal int RttMs;
        internal int LossPercent;
        internal int Ping;
        internal bool IsServer;
        internal bool IsClient;

        internal string FormatLine()
        {
            if (!Available)
            {
                return "fikaNet=unavailable";
            }

            return $"fikaNet rtt={RttMs}ms loss={LossPercent}% ping={Ping} isServer={IsServer} isClient={IsClient}";
        }
    }

    internal static class FikaNetworkStatsMonitor
    {
        private static bool _probed;
        private static PropertyInfo _rttProp;
        private static PropertyInfo _lossProp;
        private static PropertyInfo _isServerProp;
        private static PropertyInfo _isClientProp;
        private static PropertyInfo _fikaClientPingProp;
        private static Func<object> _getFikaClient;

        internal static FikaNetworkStatsSnapshot TryRead()
        {
            var snapshot = new FikaNetworkStatsSnapshot();
            if (!Probe())
            {
                return snapshot;
            }

            snapshot = new FikaNetworkStatsSnapshot
            {
                Available = true,
                IsServer = ReadBool(_isServerProp),
                IsClient = ReadBool(_isClientProp),
                RttMs = ReadInt(_rttProp),
                LossPercent = ReadInt(_lossProp),
                Ping = ReadPing()
            };

            return snapshot;
        }

        private static int ReadPing()
        {
            try
            {
                var client = _getFikaClient?.Invoke();
                if (client != null && _fikaClientPingProp != null)
                {
                    return _fikaClientPingProp.GetValue(client) is int ping ? ping : 0;
                }
            }
            catch
            {
                // ignore
            }

            return 0;
        }

        internal static void WritePeriodicIfInRaid(bool force)
        {
            if (!force && !RaidStateDetector.IsInRaidQuick())
            {
                return;
            }

            var stats = TryRead();
            if (!stats.Available && !force)
            {
                return;
            }

            if (!force && stats.RttMs <= 0 && stats.LossPercent <= 0 && stats.Ping <= 0)
            {
                return;
            }

            SessionBootstrap.Write(
                LogCategories.FikaNet,
                "INFO",
                stats.FormatLine());
        }

        private static bool Probe()
        {
            if (_probed)
            {
                return _rttProp != null;
            }

            _probed = true;

            try
            {
                var sessionType = Type.GetType("NetworkGameSession, Assembly-CSharp");
                _rttProp = sessionType?.GetProperty("Rtt", BindingFlags.Static | BindingFlags.Public);
                _lossProp = sessionType?.GetProperty("LossPercent", BindingFlags.Static | BindingFlags.Public);

                var fikaUtils = Type.GetType("Fika.Core.Main.Utils.FikaBackendUtils, Fika.Core");
                _isServerProp = fikaUtils?.GetProperty("IsServer", BindingFlags.Static | BindingFlags.Public);
                _isClientProp = fikaUtils?.GetProperty("IsClient", BindingFlags.Static | BindingFlags.Public);

                var fikaClientType = Type.GetType("Fika.Core.Networking.FikaClient, Fika.Core");
                _fikaClientPingProp = fikaClientType?.GetProperty("Ping", BindingFlags.Instance | BindingFlags.Public);

                if (fikaClientType != null)
                {
                    var singletonType = typeof(Comfort.Common.Singleton<>).MakeGenericType(fikaClientType);
                    var instanceProp = singletonType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                    if (instanceProp != null)
                    {
                        _getFikaClient = () => instanceProp.GetValue(null);
                    }
                }

                return _rttProp != null || _isServerProp != null;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadInt(PropertyInfo prop)
        {
            if (prop == null)
            {
                return 0;
            }

            try
            {
                var value = prop.GetValue(null);
                return value switch
                {
                    int i => i,
                    float f => (int)f,
                    double d => (int)d,
                    _ => 0
                };
            }
            catch
            {
                return 0;
            }
        }

        private static bool ReadBool(PropertyInfo prop)
        {
            if (prop == null)
            {
                return false;
            }

            try
            {
                return prop.GetValue(null) is bool value && value;
            }
            catch
            {
                return false;
            }
        }
    }
}
