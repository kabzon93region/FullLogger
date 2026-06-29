using System;
using System.Reflection;
using Comfort.Common;
using EFT;

namespace FullLogger.Monitoring
{
    internal struct RaidStateSnapshot
    {
        internal bool InRaid;
        internal string LocationId;
        internal string ProfileId;
        internal bool PlayerAlive;
        internal float RaidSeconds;
        internal float Fps;

        internal string FormatLine(string healthLine, string fikaLine)
        {
            if (!InRaid)
            {
                return "out_of_raid";
            }

            return
                $"loc={LocationId ?? "?"} profile={ProfileId ?? "?"} alive={PlayerAlive} " +
                $"raidSec={RaidSeconds:0} fps={Fps:0} {healthLine} {fikaLine}";
        }
    }

    internal static class RaidStateDetector
    {
        private static float _smoothedFps = 60f;
        private static FieldInfo _gameTimerField;

        internal static bool TryCapture(float deltaTime, out RaidStateSnapshot snapshot)
        {
            snapshot = default;

            if (deltaTime > 0f)
            {
                var instant = 1f / deltaTime;
                _smoothedFps = _smoothedFps <= 0f ? instant : (_smoothedFps * 0.9f + instant * 0.1f);
            }

            snapshot.Fps = _smoothedFps;

            if (!Singleton<GameWorld>.Instantiated)
            {
                return false;
            }

            var world = Singleton<GameWorld>.Instance;
            var mainPlayer = world?.MainPlayer;
            if (mainPlayer == null)
            {
                return false;
            }

            snapshot.InRaid = true;
            snapshot.ProfileId = mainPlayer.ProfileId;
            snapshot.PlayerAlive = mainPlayer.HealthController?.IsAlive ?? false;
            snapshot.LocationId = ResolveLocationId(world);
            snapshot.RaidSeconds = ResolveRaidSeconds(world);
            return true;
        }

        internal static bool IsInRaidQuick()
        {
            return Singleton<GameWorld>.Instantiated
                && Singleton<GameWorld>.Instance?.MainPlayer != null;
        }

        private static string ResolveLocationId(GameWorld world)
        {
            try
            {
                var location = world?.LocationId;
                if (!string.IsNullOrEmpty(location))
                {
                    return location;
                }

                var game = world.GetType().GetProperty("Game", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(world);
                var locProp = game?.GetType().GetProperty("Location", BindingFlags.Instance | BindingFlags.Public)
                    ?? game?.GetType().GetProperty("LocationId", BindingFlags.Instance | BindingFlags.Public);
                return locProp?.GetValue(game) as string ?? "?";
            }
            catch
            {
                return "?";
            }
        }

        private static float ResolveRaidSeconds(GameWorld world)
        {
            try
            {
                var game = world.GetType().GetProperty("Game", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(world);
                if (game == null)
                {
                    return 0f;
                }

                _gameTimerField ??= game.GetType().GetField(
                    "PastTime",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (_gameTimerField?.GetValue(game) is float pastTime)
                {
                    return pastTime;
                }

                var pastProp = game.GetType().GetProperty("PastTime", BindingFlags.Instance | BindingFlags.Public);
                if (pastProp?.GetValue(game) is float propTime)
                {
                    return propTime;
                }
            }
            catch
            {
                // ignore
            }

            return 0f;
        }
    }
}
