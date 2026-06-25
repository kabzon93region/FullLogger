using System;
using BepInEx.Logging;
using FullLogger.Logging;
using HarmonyLib;

namespace FullLogger.Tracing
{
    /// <summary>
    /// Lightweight hooks for high-value raid events (no dynamic method trace).
    /// </summary>
    internal static class GameEventTracer
    {
        private const string ClientInventoryControllerType =
            "Fika.Core.Main.ClientClasses.ClientInventoryController";

        private static bool _applied;

        internal static bool TryApply(
            Harmony harmony,
            ManualLogSource logger,
            bool traceInventory,
            bool traceRicochet)
        {
            if (_applied || (!traceInventory && !traceRicochet))
            {
                return _applied;
            }

            var appliedAny = false;

            if (traceInventory)
            {
                appliedAny |= TryPatchInventory(harmony, logger);
            }

            if (traceRicochet)
            {
                appliedAny |= TryPatchRicochet(harmony, logger);
            }

            _applied = appliedAny;
            if (appliedAny)
            {
                logger.LogInfo("[FULL_LOGGER] Game event tracer active (inventory/ricochet)");
            }

            return _applied;
        }

        private static bool TryPatchInventory(Harmony harmony, ManualLogSource logger)
        {
            var controllerType = AccessTools.TypeByName(ClientInventoryControllerType);
            var runOp = AccessTools.Method(controllerType, "RunClientOperation");
            if (runOp == null)
            {
                logger.LogDebug("[FULL_LOGGER] Inventory trace skipped (Fika client controller not found)");
                return false;
            }

            harmony.Patch(
                runOp,
                postfix: new HarmonyMethod(typeof(GameEventTracer), nameof(InventoryOperationPostfix)));
            return true;
        }

        private static bool TryPatchRicochet(Harmony harmony, ManualLogSource logger)
        {
            var effectsType = AccessTools.TypeByName("Systems.Effects.EffectsCommutator");
            var playHit = AccessTools.Method(effectsType, "PlayHitEffect");
            if (playHit == null)
            {
                logger.LogDebug("[FULL_LOGGER] Ricochet trace skipped (EffectsCommutator not found)");
                return false;
            }

            harmony.Patch(
                playHit,
                prefix: new HarmonyMethod(typeof(GameEventTracer), nameof(RicochetHitPrefix)));
            return true;
        }

        private static void InventoryOperationPostfix(object operation)
        {
            if (operation == null)
            {
                return;
            }

            try
            {
                var typeName = operation.GetType().Name;
                string detail = typeName;

                var itemProp = AccessTools.Property(operation.GetType(), "Item");
                var item = itemProp?.GetValue(operation);
                if (item != null)
                {
                    var templateProp = AccessTools.Property(item.GetType(), "TemplateId");
                    var idProp = AccessTools.Property(item.GetType(), "Id");
                    var template = templateProp?.GetValue(item)?.ToString() ?? "?";
                    var id = idProp?.GetValue(item)?.ToString() ?? "?";
                    detail = $"{typeName} tpl={template} id={id}";
                }

                SessionBootstrap.Write(LogCategories.GameEvent, "INFO", $"INV_OP {detail}");
            }
            catch
            {
                // never break inventory
            }
        }

        private static void RicochetHitPrefix(object info)
        {
            if (info == null)
            {
                return;
            }

            try
            {
                var bulletStateField = AccessTools.Field(info.GetType(), "BulletState");
                var bulletState = bulletStateField?.GetValue(info);
                if (bulletState == null || bulletState.ToString().IndexOf("Ricochet", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                var hitPointField = AccessTools.Field(info.GetType(), "HitPoint");
                var hit = hitPointField?.GetValue(info);
                SessionBootstrap.Write(LogCategories.GameEvent, "INFO", $"COMBAT ricochet at {hit}");
            }
            catch
            {
                // ignore
            }
        }
    }
}
