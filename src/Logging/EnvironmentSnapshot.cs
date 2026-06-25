using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using FullLogger.Logging;
using HarmonyLib;
using UnityEngine;

namespace FullLogger
{
    internal static class EnvironmentSnapshot
    {
        private const string FikaBackendUtilsType = "Fika.Core.Main.Utils.FikaBackendUtils";
        private static string _lastFingerprint = string.Empty;

        internal static void WriteStartupSnapshot(PluginCore plugin)
        {
            WriteBlock("=== STARTUP ENVIRONMENT ===");
            WriteLine("ROLE", DescribeLocalRole());
            WriteLine("MACHINE", $"{Environment.MachineName} / user={Environment.UserName}");
            WriteLine("GAME_ROOT", BepInEx.Paths.GameRootPath ?? ResolveGameRoot());
            WriteLine("BEPINEX_ROOT", ResolveBepInRoot());
            WriteLine("PLUGIN_PATH", BepInEx.Paths.PluginPath ?? "(null)");
            WriteLine("CONFIG_PATH", BepInEx.Paths.ConfigPath ?? "(null)");
            WriteLine("EXECUTABLE", Environment.GetCommandLineArgs().FirstOrDefault() ?? "(unknown)");
            WriteLine("WORKING_DIR", Directory.GetCurrentDirectory());
            WriteLine("PROCESS", $"pid={System.Diagnostics.Process.GetCurrentProcess().Id}, arch={(Environment.Is64BitProcess ? "x64" : "x86")}");
            WriteLine("UNITY", $"{Application.unityVersion}, product={Application.productName}, dataPath={Application.dataPath}");
            WriteLine("SYSTEM", $"{SystemInfo.operatingSystem}, device={SystemInfo.deviceModel}, gpu={SystemInfo.graphicsDeviceName}");
            WriteLine("MEMORY", $"managed={GC.GetTotalMemory(false) / (1024 * 1024)}MB, system={SystemInfo.systemMemorySize}MB");

            WriteNetworkBlock();
            WriteConfigBlock(plugin);
            WritePluginDetailBlock();
            WriteFikaBlock(force: true);
            WriteBlock("=== END STARTUP ENVIRONMENT ===");
        }

        internal static void WritePeriodicUpdateIfChanged()
        {
            var fp = BuildFikaFingerprint();
            if (string.Equals(fp, _lastFingerprint, StringComparison.Ordinal))
            {
                return;
            }

            _lastFingerprint = fp;
            WriteBlock("=== RUNTIME UPDATE ===");
            WriteLine("ROLE", DescribeLocalRole());
            WriteFikaBlock(force: true);
            if (FikaSessionGuard.IsTarkovApplicationReady())
            {
                WriteProfileBlock();
            }
            else
            {
                WriteLine("PROFILE", "(session not ready)");
            }

            WriteBlock("=== END RUNTIME UPDATE ===");
        }

        private static void WriteNetworkBlock()
        {
            WriteBlock("-- Network --");
            try
            {
                var host = Dns.GetHostName();
                WriteLine("DNS_HOST", host);

                foreach (var addr in Dns.GetHostAddresses(host).OrderBy(a => a.ToString()))
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork ||
                        addr.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        WriteLine("IP", addr.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine("DNS_ERROR", ex.Message);
            }

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                             .Where(n => n.OperationalStatus == OperationalStatus.Up))
                {
                    foreach (var uni in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily == AddressFamily.InterNetwork ||
                            uni.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            WriteLine("NIC", $"{nic.Name}: {uni.Address}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine("NIC_ERROR", ex.Message);
            }
        }

        private static void WriteConfigBlock(PluginCore plugin)
        {
            if (plugin == null)
            {
                return;
            }

            WriteBlock("-- FullLogger config --");
            WriteLine("CFG", $"Enabled={plugin.Enabled.Value}");
            WriteLine("CFG", $"MirrorUnityLog={plugin.MirrorUnityLog.Value}");
            WriteLine("CFG", $"MirrorBepInExLog={plugin.MirrorBepInExLog.Value}");
            WriteLine("CFG", $"MirrorLogOutputFile={plugin.MirrorLogOutputFile.Value}");
            WriteLine("CFG", $"MirrorGameLogs={plugin.MirrorGameLogs.Value}");
            WriteLine("CFG", $"LogHarmonyPatchAudit={plugin.LogHarmonyPatchAudit.Value}");
            WriteLine("CFG", $"DynamicTraceMods={plugin.DynamicTraceMods.Value}");
            WriteLine("CFG", $"DynamicTraceGame={plugin.DynamicTraceGame.Value}");
            WriteLine("CFG", $"SessionRoot={plugin.SessionRoot.Value}");
        }

        private static void WritePluginDetailBlock()
        {
            WriteBlock("-- BepInEx plugins (detail) --");
            try
            {
                foreach (var pair in Chainloader.PluginInfos.OrderBy(p => p.Value.Metadata.Name))
                {
                    var info = pair.Value;
                    var meta = info.Metadata;
                    var location = TryGetPluginLocation(info);
                    WriteLine("PLUGIN",
                        $"{meta.Name} v{meta.Version} guid={meta.GUID} path={location}");
                }
            }
            catch (Exception ex)
            {
                WriteLine("PLUGIN_ERROR", ex.ToString());
            }
        }

        private static string TryGetPluginLocation(BepInEx.PluginInfo pluginInfo)
        {
            try
            {
                var instance = pluginInfo.Instance;
                if (instance != null)
                {
                    var locProp = instance.GetType().GetProperty("Info", BindingFlags.Instance | BindingFlags.Public);
                    var instanceInfo = locProp?.GetValue(instance);
                    var locationProp = instanceInfo?.GetType().GetProperty("Location");
                    var location = locationProp?.GetValue(instanceInfo) as string;
                    if (!string.IsNullOrEmpty(location))
                    {
                        return location;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return "(unknown)";
        }

        private static void WriteFikaBlock(bool force)
        {
            var fikaType = AccessTools.TypeByName(FikaBackendUtilsType);
            if (fikaType == null)
            {
                WriteLine("FIKA", "not loaded");
                return;
            }

            WriteBlock("-- Fika --");
            WriteProp(fikaType, "ClientType");
            WriteProp(fikaType, "IsServer");
            WriteProp(fikaType, "IsClient");
            WriteProp(fikaType, "IsHeadless");
            WriteProp(fikaType, "IsHeadlessGame");
            WriteProp(fikaType, "IsHeadlessRequester");
            WriteProp(fikaType, "IsSpectator");
            WriteProp(fikaType, "IsReconnect");
            WriteProp(fikaType, "IsTransit");
            WriteProp(fikaType, "IsScav");
            WriteProp(fikaType, "GroupId");
            WriteProp(fikaType, "RaidCode");
            WriteProp(fikaType, "PMCName");
            WriteProp(fikaType, "HostLocationId");
            WriteProp(fikaType, "LocalPort");
            WriteProp(fikaType, "RemoteEndPoint");
            WriteProp(fikaType, "ServerGuid");
            WriteProp(fikaType, "IsSinglePlayer");
        }

        private static void WriteProfileBlock()
        {
            var fikaType = AccessTools.TypeByName(FikaBackendUtilsType);
            if (fikaType == null || !FikaSessionGuard.IsTarkovApplicationReady())
            {
                return;
            }

            try
            {
                var profile = FikaSessionGuard.TryGetSessionProfile();
                if (profile == null)
                {
                    WriteLine("PROFILE", "(null)");
                    return;
                }

                var profileType = profile.GetType();
                WriteLine("PROFILE_ID", ReadMember(profile, profileType, "ProfileId"));
                WriteLine("PROFILE_NICK", ReadMember(profile, profileType, "Nickname"));
                WriteLine("PROFILE_SIDE", ReadMember(profile, profileType, "Side"));

                var info = ReadMemberValue(profile, profileType, "Info");
                WriteLine("PROFILE_LEVEL", info != null
                    ? ReadMember(info, info.GetType(), "Level")
                    : "(n/a)");
            }
            catch (Exception ex)
            {
                WriteLine("PROFILE_ERROR", ex.Message);
            }
        }

        private const BindingFlags InstanceMemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags StaticMemberFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static object ReadStaticMemberValue(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var prop = type.GetProperty(name, StaticMemberFlags);
            if (prop != null)
            {
                return prop.GetValue(null);
            }

            var field = type.GetField(name, StaticMemberFlags);
            return field?.GetValue(null);
        }

        private static object ReadMemberValue(object target, Type type, string name)
        {
            if (target == null || type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var prop = type.GetProperty(name, InstanceMemberFlags);
            if (prop != null)
            {
                return prop.GetValue(target);
            }

            var field = type.GetField(name, InstanceMemberFlags);
            return field?.GetValue(target);
        }

        private static string ReadMember(object target, Type type, string name)
        {
            try
            {
                return ReadMemberValue(target, type, name)?.ToString() ?? "(null)";
            }
            catch
            {
                return "(error)";
            }
        }

        private static void WriteProp(Type type, string name)
        {
            try
            {
                var value = ReadStaticMemberValue(type, name);
                if (value != null || type.GetProperty(name, StaticMemberFlags) != null ||
                    type.GetField(name, StaticMemberFlags) != null)
                {
                    WriteLine("FIKA", $"{name}={value}");
                }
            }
            catch (Exception ex)
            {
                WriteLine("FIKA", $"{name}=<error: {ex.Message}>");
            }
        }

        private static string DescribeLocalRole()
        {
            if (Chainloader.PluginInfos.ContainsKey("com.fika.headless"))
            {
                return "headless_host (Fika.Headless loaded)";
            }

            var fikaType = AccessTools.TypeByName(FikaBackendUtilsType);
            if (fikaType == null)
            {
                return "standalone_client (no Fika)";
            }

            try
            {
                var isServer = ReadStaticMemberValue(fikaType, "IsServer") as bool? ?? false;
                var isClient = ReadStaticMemberValue(fikaType, "IsClient") as bool? ?? false;
                var isHeadless = ReadStaticMemberValue(fikaType, "IsHeadless") as bool? ?? false;

                if (isHeadless)
                {
                    return "headless_host";
                }

                if (isServer && isClient)
                {
                    return "listen_host (host+play same PC)";
                }

                if (isServer)
                {
                    return "fika_host";
                }

                if (isClient)
                {
                    return "fika_client (headless_client scenario)";
                }

                return "fika_idle (menu/offline)";
            }
            catch
            {
                return "fika_unknown";
            }
        }

        private static string BuildFikaFingerprint()
        {
            var sb = new StringBuilder();
            var fikaType = AccessTools.TypeByName(FikaBackendUtilsType);
            if (fikaType == null)
            {
                return "no-fika";
            }

            foreach (var name in new[]
                     {
                         "ClientType", "RaidCode", "GroupId", "PMCName", "HostLocationId",
                         "IsHeadlessGame", "IsSpectator", "IsReconnect"
                     })
            {
                try
                {
                    var value = ReadStaticMemberValue(fikaType, name);
                    sb.Append(name).Append('=').Append(value).Append(';');
                }
                catch
                {
                    sb.Append(name).Append("=?;");
                }
            }

            return sb.ToString();
        }

        private static void WriteBlock(string title)
        {
            SessionBootstrap.Write(LogCategories.Env, "INFO", title);
        }

        private static void WriteLine(string key, string value)
        {
            SessionBootstrap.Write(LogCategories.Env, "INFO", $"{key}: {value}");
        }

        private static string ResolveBepInRoot()
        {
            try
            {
                return Path.GetFullPath(Path.Combine(BepInEx.Paths.PluginPath, ".."));
            }
            catch
            {
                return "(unknown)";
            }
        }

        private static string ResolveGameRoot()
        {
            try
            {
                return Path.GetFullPath(Path.Combine(BepInEx.Paths.PluginPath, "..", ".."));
            }
            catch
            {
                return "(unknown)";
            }
        }
    }
}
