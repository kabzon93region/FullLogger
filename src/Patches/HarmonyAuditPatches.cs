using System;
using System.Reflection;
using HarmonyLib;
using FullLogger.Logging;
using FullLogger.Tracing;

namespace FullLogger.Patches
{
    [HarmonyPatch(typeof(Harmony))]
    internal static class HarmonyAuditPatches
    {
        [HarmonyPatch(nameof(Harmony.Patch), new[]
        {
            typeof(MethodBase),
            typeof(HarmonyMethod),
            typeof(HarmonyMethod),
            typeof(HarmonyMethod),
            typeof(HarmonyMethod)
        })]
        [HarmonyPostfix]
        private static void PatchPostfix(MethodBase original, Harmony __instance)
        {
            if (original == null || HarmonyAuditSuppressor.Suppress)
            {
                return;
            }

            SessionBootstrap.Write(
                LogCategories.Harmony,
                "INFO",
                $"PATCH id={__instance?.Id} {original.DeclaringType?.FullName}.{original.Name}");
        }

        [HarmonyPatch(nameof(Harmony.Unpatch), new[] { typeof(MethodBase), typeof(HarmonyPatchType), typeof(string) })]
        [HarmonyPostfix]
        private static void UnpatchPostfix(MethodBase original, Harmony __instance)
        {
            if (original == null || HarmonyAuditSuppressor.Suppress)
            {
                return;
            }

            SessionBootstrap.Write(
                LogCategories.Harmony,
                "INFO",
                $"UNPATCH id={__instance?.Id} {original.DeclaringType?.FullName}.{original.Name}");
        }
    }
}
