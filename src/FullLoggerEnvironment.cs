using System;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace FullLogger
{
    internal static class FullLoggerEnvironment
    {
        private const string FikaHeadlessGuid = "com.fika.headless";
        private const string FikaBackendUtilsType = "Fika.Core.Main.Utils.FikaBackendUtils";

        internal static bool IsHeadlessOrDedicatedServer()
        {
            if (Chainloader.PluginInfos.ContainsKey(FikaHeadlessGuid))
            {
                return true;
            }

            var fikaType = AccessTools.TypeByName(FikaBackendUtilsType);
            if (fikaType == null)
            {
                return false;
            }

            try
            {
                var isServer = AccessTools.Property(fikaType, "IsServer")?.GetValue(null);
                return isServer is bool server && server;
            }
            catch
            {
                return false;
            }
        }
    }
}
