using System.Reflection;
using BepInEx.Logging;
using FullLogger.Logging;
using HarmonyLib;

namespace FullLogger.Tracing
{
    /// <summary>
    /// Lightweight F8 / extract hooks — one prefix per frame when key down, throttled in handler.
    /// </summary>
    internal static class RaidEventTracer
    {
        private static bool _applied;
        private static float _lastF8LogTime;

        internal static bool TryApply(Harmony harmony, ManualLogSource logger)
        {
            if (_applied)
            {
                return true;
            }

            var coopHandlerType = AccessTools.TypeByName("Fika.Core.Main.Components.CoopHandler");
            if (coopHandlerType == null)
            {
                logger.LogDebug("[FULL_LOGGER] Raid extract trace skipped (no Fika CoopHandler)");
                return false;
            }

            var processQuitting = AccessTools.Method(coopHandlerType, "ProcessQuitting");
            if (processQuitting == null)
            {
                return false;
            }

            harmony.Patch(processQuitting, prefix: new HarmonyMethod(typeof(RaidEventTracer), nameof(ProcessQuittingPrefix)));
            _applied = true;
            logger.LogInfo("[FULL_LOGGER] Raid event tracer active (F8/extract via CoopHandler)");
            return true;
        }

        private static void ProcessQuittingPrefix(object __instance)
        {
            if (PluginCore.Instance?.CaptureController?.IsCaptureActive != true
                || PluginCore.Instance.TraceExtractEvents.Value != true)
            {
                return;
            }

            if (!IsExtractKeyDown(__instance))
            {
                return;
            }

            var now = UnityEngine.Time.unscaledTime;
            if (now - _lastF8LogTime < 1.5f)
            {
                return;
            }

            _lastF8LogTime = now;
            var quitState = __instance?.GetType().GetProperty("QuitState")?.GetValue(__instance)?.ToString() ?? "?";
            var requestQuit = ReadBoolField(__instance, "_requestQuitGame");
            var isClient = ReadBoolField(__instance, "_isClient");
            var exitLocation = ReadExitLocation(__instance);

            SessionBootstrap.Write(
                LogCategories.Extract,
                "INFO",
                $"[F8_EXTRACT] quitState={quitState} requestQuit={requestQuit} isClient={isClient} exitLocation='{exitLocation}'");

            if (quitState == "None")
            {
                SessionBootstrap.Write(
                    LogCategories.Extract,
                    "WARN",
                    "[F8_EXTRACT] blocked — QuitState=None (not at extract screen / desync?)");
            }
        }

        private static bool IsExtractKeyDown(object coopHandler)
        {
            try
            {
                var fikaPlugin = AccessTools.TypeByName("Fika.Core.FikaPlugin");
                var instance = fikaPlugin?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                var settings = instance?.GetType().GetProperty("Settings")?.GetValue(instance);
                var extractKey = settings?.GetType().GetProperty("ExtractKey")?.GetValue(settings);
                if (extractKey == null)
                {
                    return false;
                }

                return extractKey.GetType().GetMethod("IsDown")?.Invoke(extractKey, null) is bool down && down;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadExitLocation(object coopHandler)
        {
            try
            {
                var game = coopHandler?.GetType()
                    .GetProperty("LocalGameInstance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(coopHandler);
                return game?.GetType().GetProperty("ExitLocation")?.GetValue(game) as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ReadBoolField(object instance, string fieldName)
        {
            try
            {
                var field = instance?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                return field?.GetValue(instance) is bool value && value;
            }
            catch
            {
                return false;
            }
        }
    }
}
