using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using FullLogger.Logging;
using HarmonyLib;
using UnityEngine;

namespace FullLogger
{
    internal static class SessionBootstrap
    {
        private static bool _ready;

        internal static void InitializeEarly(string rootDir, int maxPartSizeMb, bool backgroundWrite, int maxPending)
        {
            if (_ready)
            {
                return;
            }

            var logger = SessionFileLogger.Create(rootDir, maxPartSizeMb, backgroundWrite, maxPending);
            SessionSummaryWriter.BindSession(logger.SessionDirectory);
            _ready = true;
            Write(LogCategories.Session, "INFO", "Full Logger session started");
            Write(LogCategories.Session, "INFO", $"Session directory: {SessionFileLogger.Instance.SessionDirectory}");
            Write(LogCategories.Session, "INFO", $"Unity {Application.unityVersion}, OS {SystemInfo.operatingSystem}");
            Write(LogCategories.Session, "INFO", $"CLR {Environment.Version}, cwd {Directory.GetCurrentDirectory()}");
        }

        internal static void Write(string category, string level, string message)
        {
            SessionSummaryWriter.Record(category, level, message);
            var logger = SessionFileLogger.Instance;
            logger?.Write(category, level, message);
        }

        internal static void WritePluginInventory()
        {
            try
            {
                var infos = Chainloader.PluginInfos;
                Write(LogCategories.Plugin, "INFO", $"Loaded BepInEx plugins: {infos.Count}");
                foreach (var pair in infos.OrderBy(p => p.Value.Metadata.Name))
                {
                    var meta = pair.Value.Metadata;
                    Write(LogCategories.Plugin, "INFO",
                        $"PLUGIN {meta.Name} v{meta.Version} guid={meta.GUID}");
                }
            }
            catch (Exception ex)
            {
                Write(LogCategories.Plugin, "ERROR", $"Failed to enumerate plugins: {ex}");
            }

            try
            {
                var sb = new StringBuilder();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name))
                {
                    var name = asm.GetName().Name;
                    if (name.StartsWith("System", StringComparison.Ordinal) ||
                        name.StartsWith("Microsoft", StringComparison.Ordinal) ||
                        name.StartsWith("Unity", StringComparison.Ordinal) ||
                        name.StartsWith("Mono.", StringComparison.Ordinal) ||
                        name == "mscorlib" ||
                        name == "netstandard")
                    {
                        continue;
                    }

                    sb.Append(name).Append(';');
                }

                Write(LogCategories.Plugin, "INFO", "Assemblies: " + sb);
            }
            catch (Exception ex)
            {
                Write(LogCategories.Plugin, "ERROR", $"Failed to enumerate assemblies: {ex}");
            }
        }
    }
}
