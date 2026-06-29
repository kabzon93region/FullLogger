using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using FullLogger.Logging;

namespace FullLogger.Monitoring
{
    internal struct MetabolismSample
    {
        internal float EnergyCurrent;
        internal float EnergyMax;
        internal float HydrationCurrent;
        internal float HydrationMax;
        internal float EnergyRate;
        internal float HydrationRate;
        internal bool MetabolismDisabled;
        internal int ActiveEffectCount;
        internal bool InRaid;
        internal bool PlayerAlive;

        internal bool IsAtMax =>
            PlayerAlive && EnergyMax > 0f && HydrationMax > 0f
            && EnergyCurrent >= EnergyMax - 0.5f
            && HydrationCurrent >= HydrationMax - 0.5f;

        internal bool RatesNearZero =>
            System.Math.Abs(EnergyRate) < 0.001f && System.Math.Abs(HydrationRate) < 0.001f;

        internal string FormatLine()
        {
            return
                $"energy={EnergyCurrent:0.0}/{EnergyMax:0.0} hydration={HydrationCurrent:0.0}/{HydrationMax:0.0} " +
                $"rates e={EnergyRate:0.000} h={HydrationRate:0.000} metabolismOff={MetabolismDisabled} " +
                $"effects={ActiveEffectCount} alive={PlayerAlive}";
        }
    }

    internal static class MetabolismSampler
    {
        internal static bool TrySample(out MetabolismSample sample)
        {
            sample = default;

            if (!Singleton<GameWorld>.Instantiated)
            {
                return false;
            }

            var world = Singleton<GameWorld>.Instance;
            var player = world?.MainPlayer;
            if (player == null)
            {
                return false;
            }

            var health = player.ActiveHealthController as ActiveHealthController;
            if (health == null)
            {
                return false;
            }

            var effects = health.GetAllActiveEffects(EBodyPart.Common);
            sample = new MetabolismSample
            {
                InRaid = true,
                PlayerAlive = health.IsAlive,
                EnergyCurrent = health.Energy.Current,
                EnergyMax = health.Energy.Maximum,
                HydrationCurrent = health.Hydration.Current,
                HydrationMax = health.Hydration.Maximum,
                EnergyRate = health.EnergyRate,
                HydrationRate = health.HydrationRate,
                MetabolismDisabled = ReadMetabolismDisabled(health),
                ActiveEffectCount = effects != null ? System.Linq.Enumerable.Count(effects) : 0
            };
            return true;
        }

        private static bool ReadMetabolismDisabled(ActiveHealthController health)
        {
            var field = MetabolismReflection.MetabolismDisabledField;
            if (field == null || health == null)
            {
                return false;
            }

            try
            {
                return field.GetValue(health) is bool value && value;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class MetabolismResyncService
    {
        internal static bool TryResync(ActiveHealthController health, out string action)
        {
            action = "none";
            if (health == null || !health.IsAlive)
            {
                action = "skip-not-alive";
                return false;
            }

            var reenabled = TryReEnableMetabolism(health);
            if (reenabled)
            {
                action = "reenable-metabolism";
                return true;
            }

            var beforeEnergy = health.Energy.Current;
            var beforeHydration = health.Hydration.Current;

            try
            {
                health.ChangeEnergy(-0.5f);
                health.ChangeHydration(-0.5f);
            }
            catch
            {
                action = "probe-change-failed";
                return false;
            }

            var energyMoved = System.Math.Abs(health.Energy.Current - beforeEnergy) > 0.01f;
            var hydrationMoved = System.Math.Abs(health.Hydration.Current - beforeHydration) > 0.01f;

            if (energyMoved || hydrationMoved)
            {
                try
                {
                    health.ChangeEnergy(+0.5f);
                    health.ChangeHydration(+0.5f);
                }
                catch
                {
                    // ignore restore errors
                }

                action = "probe-nudge-ok";
                return true;
            }

            action = "probe-no-change";
            return false;
        }

        private static bool TryReEnableMetabolism(ActiveHealthController health)
        {
            var field = MetabolismReflection.MetabolismDisabledField;
            if (field == null)
            {
                return false;
            }

            try
            {
                if (field.GetValue(health) is bool disabled && disabled)
                {
                    field.SetValue(health, false);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
