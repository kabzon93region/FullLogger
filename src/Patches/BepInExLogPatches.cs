using System;
using BepInEx.Logging;
using HarmonyLib;
using FullLogger.Logging;

namespace FullLogger.Patches
{
    [HarmonyPatch(typeof(ManualLogSource), nameof(ManualLogSource.Log), new[] { typeof(LogLevel), typeof(object) })]
    internal static class BepInExLogPatches
    {
        [ThreadStatic]
        private static bool _writing;

        [HarmonyPostfix]
        private static void LogPostfix(ManualLogSource __instance, LogLevel level, object data)
        {
            if (_writing || IsSelfSource(__instance))
            {
                return;
            }

            var text = data?.ToString() ?? string.Empty;
            var formatted = LogCaptureCoordinator.FormatHookLine(__instance?.SourceName ?? "BepInEx", level.ToString(), text);
            if (!LogCaptureCoordinator.ShouldWrite(LogCategories.BepInEx, level.ToString(), formatted))
            {
                return;
            }

            _writing = true;
            try
            {
                SessionBootstrap.Write(
                    LogCategories.BepInEx,
                    level.ToString().ToUpperInvariant(),
                    formatted);
            }
            finally
            {
                _writing = false;
            }
        }

        private static bool IsSelfSource(ManualLogSource source)
        {
            var name = source?.SourceName ?? string.Empty;
            return name.IndexOf("Full Logger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("fulllogger", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
