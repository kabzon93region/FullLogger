using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using FullLogger.Logging;
using HarmonyLib;

namespace FullLogger.Tracing
{
    internal sealed class DynamicTraceSettings
    {
        internal bool TraceModAssemblies { get; set; } = true;
        internal bool TraceGameNamespaces { get; set; } = true;
        internal bool TraceConstructors { get; set; } = true;
        internal bool TracePropertyAccessors { get; set; }
        internal int ThrottleUnityTickMs { get; set; } = 1000;
        internal string GameNamespacePrefixes { get; set; } = "EFT,Comfort,SPT";
        internal string ModPathMarker { get; set; } = "BepInEx\\plugins";
    }

    internal static class DynamicMethodTracer
    {
        private static readonly ConcurrentDictionary<int, long> TickCounters = new ConcurrentDictionary<int, long>();
        private static readonly ConcurrentDictionary<int, DateTime> TickLastLog = new ConcurrentDictionary<int, DateTime>();
        private static readonly ThreadLocal<Stopwatch> Stopwatches = new ThreadLocal<Stopwatch>(() => new Stopwatch());

        [ThreadStatic]
        private static bool _insideTracer;

        private static HarmonyMethod _prefix;
        private static HarmonyMethod _finalizer;
        private static DynamicTraceSettings _settings;

        internal static IEnumerator ApplyAsync(Harmony harmony, DynamicTraceSettings settings, int patchesPerFrame = 200)
        {
            _settings = settings ?? new DynamicTraceSettings();
            _prefix = new HarmonyMethod(typeof(DynamicMethodTracer), nameof(UniversalPrefix));
            _finalizer = new HarmonyMethod(typeof(DynamicMethodTracer), nameof(UniversalFinalizer));

            HarmonyAuditSuppressor.EnterBulk();
            var patched = 0;

            try
            {
                var prefixes = ParsePrefixes(_settings.GameNamespacePrefixes).ToList();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var isGameAssembly = string.Equals(
                        assembly.GetName().Name,
                        "Assembly-CSharp",
                        StringComparison.OrdinalIgnoreCase);

                    if (!ShouldScanAssembly(assembly, prefixes, _settings))
                    {
                        continue;
                    }

                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.Where(t => t != null).ToArray();
                    }

                    foreach (var type in types)
                    {
                        if (type == null || !ShouldScanType(type))
                        {
                            continue;
                        }

                        if (isGameAssembly && _settings.TraceGameNamespaces)
                        {
                            var ns = type.Namespace ?? string.Empty;
                            if (!prefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal)))
                            {
                                continue;
                            }
                        }

                        MethodInfo[] methods;
                        try
                        {
                            methods = type.GetMethods(
                                BindingFlags.Instance |
                                BindingFlags.Static |
                                BindingFlags.Public |
                                BindingFlags.NonPublic |
                                BindingFlags.DeclaredOnly);
                        }
                        catch
                        {
                            continue;
                        }

                        foreach (var method in methods)
                        {
                            if (!ShouldPatchMethod(method, _settings))
                            {
                                continue;
                            }

                            var key = method.DeclaringType?.FullName + "::" + method.MetadataToken;
                            if (!seen.Add(key))
                            {
                                continue;
                            }

                            try
                            {
                                harmony.Patch(method, prefix: _prefix, finalizer: _finalizer);
                                patched++;
                            }
                            catch
                            {
                                // skip methods Harmony cannot patch
                            }

                            if (patched > 0 && patched % patchesPerFrame == 0)
                            {
                                yield return null;
                            }
                        }
                    }
                }

                SessionBootstrap.Write(LogCategories.Trace, "INFO", $"Dynamic method tracer applied to {patched} methods");
            }
            finally
            {
                HarmonyAuditSuppressor.ExitBulk();
            }
        }

        private static IEnumerable<string> ParsePrefixes(string csv)
        {
            return (csv ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0);
        }

        private static bool ShouldScanAssembly(Assembly assembly, IEnumerable<string> gamePrefixes, DynamicTraceSettings settings)
        {
            var name = assembly.GetName().Name ?? string.Empty;
            if (name.Equals("FullLogger", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (name.StartsWith("System", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft", StringComparison.Ordinal) ||
                name.StartsWith("Unity", StringComparison.Ordinal) ||
                name.StartsWith("Mono.", StringComparison.Ordinal) ||
                name == "mscorlib" ||
                name == "netstandard" ||
                name == "0Harmony" ||
                name == "BepInEx")
            {
                return false;
            }

            if (name.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
            {
                return settings.TraceGameNamespaces;
            }

            var location = SafeLocation(assembly);
            if (settings.TraceModAssemblies &&
                !string.IsNullOrEmpty(location) &&
                location.IndexOf(settings.ModPathMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (settings.TraceGameNamespaces)
            {
                foreach (var prefix in gamePrefixes)
                {
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string SafeLocation(Assembly assembly)
        {
            try
            {
                return assembly.Location ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ShouldScanType(Type type)
        {
            if (type.IsGenericTypeDefinition || type.IsGenericType || type.ContainsGenericParameters)
            {
                return false;
            }

            if (type.IsInterface || type.IsEnum)
            {
                return false;
            }

            return true;
        }

        private static bool ShouldPatchMethod(MethodInfo method, DynamicTraceSettings settings)
        {
            if (method == null || method.IsAbstract || method.ContainsGenericParameters)
            {
                return false;
            }

            var name = method.Name;
            if (name.StartsWith("<", StringComparison.Ordinal))
            {
                return false;
            }

            var declaringType = method.DeclaringType;
            if (declaringType == null || !ShouldScanType(declaringType))
            {
                return false;
            }

            if (!IsDeclaredOnScannedType(method))
            {
                return false;
            }

            var ns = declaringType.Namespace ?? string.Empty;
            if (IsExcludedNamespace(ns) || IsExcludedTypeName(declaringType.Name))
            {
                return false;
            }

            if (IsUnsafeIntrinsicMethod(name, declaringType) || IsUnityLifecycleMethod(name))
            {
                return false;
            }

            if (method.IsConstructor)
            {
                return settings.TraceConstructors;
            }

            if (name.StartsWith("get_", StringComparison.Ordinal) ||
                name.StartsWith("set_", StringComparison.Ordinal) ||
                name.StartsWith("add_", StringComparison.Ordinal) ||
                name.StartsWith("remove_", StringComparison.Ordinal))
            {
                return settings.TracePropertyAccessors;
            }

            return true;
        }

        private static bool IsDeclaredOnScannedType(MethodInfo method)
        {
            var scanType = method.DeclaringType;
            if (scanType == null)
            {
                return false;
            }

            var ns = scanType.Namespace ?? string.Empty;
            if (IsExcludedNamespace(ns))
            {
                return false;
            }

            return true;
        }

        private static bool IsExcludedNamespace(string ns)
        {
            return ns.StartsWith("Harmony", StringComparison.Ordinal) ||
                   ns.StartsWith("BepInEx", StringComparison.Ordinal) ||
                   ns.StartsWith("FullLogger", StringComparison.Ordinal) ||
                   ns.StartsWith("System", StringComparison.Ordinal) ||
                   ns.StartsWith("Microsoft", StringComparison.Ordinal) ||
                   ns.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                   ns.StartsWith("Unity.", StringComparison.Ordinal) ||
                   ns.StartsWith("NLog", StringComparison.Ordinal) ||
                   ns.StartsWith("Sirenix", StringComparison.Ordinal);
        }

        private static bool IsExcludedTypeName(string typeName)
        {
            return typeName == "LoggerClass" ||
                   typeName.StartsWith("Harmony", StringComparison.Ordinal);
        }

        private static bool IsUnsafeIntrinsicMethod(string name, Type declaringType)
        {
            if (name == "ToString" ||
                name == "GetHashCode" ||
                name == "Equals" ||
                name == "GetType" ||
                name == "MemberwiseClone" ||
                name == "Finalize" ||
                name == "ReferenceEquals")
            {
                return true;
            }

            if (declaringType == typeof(object) || declaringType == typeof(ValueType))
            {
                return true;
            }

            return false;
        }

        private static bool IsUnityLifecycleMethod(string name)
        {
            switch (name)
            {
                case "Awake":
                case "Start":
                case "OnEnable":
                case "OnDisable":
                case "OnDestroy":
                case "Update":
                case "FixedUpdate":
                case "LateUpdate":
                case "OnValidate":
                case "OnApplicationFocus":
                case "OnApplicationPause":
                case "OnApplicationQuit":
                case "OnDrawGizmos":
                case "OnDrawGizmosSelected":
                case "Reset":
                case "OnGUI":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsUnityTick(MethodBase method)
        {
            var name = method.Name;
            return name == "Update" || name == "FixedUpdate" || name == "LateUpdate";
        }

        public static void UniversalPrefix(MethodBase __originalMethod, object[] __args, object __instance)
        {
            if (__originalMethod == null || _insideTracer || TraceFormatter.IsFormatting)
            {
                return;
            }

            if (_settings != null && IsUnityTick(__originalMethod) && _settings.ThrottleUnityTickMs > 0)
            {
                var token = __originalMethod.MetadataToken;
                TickCounters.AddOrUpdate(token, 1, (_, c) => c + 1);

                var now = DateTime.UtcNow;
                var last = TickLastLog.GetOrAdd(token, DateTime.MinValue);
                if ((now - last).TotalMilliseconds < _settings.ThrottleUnityTickMs)
                {
                    return;
                }

                TickLastLog[token] = now;
                var calls = TickCounters.AddOrUpdate(token, 0, (_, c) => c);
                TickCounters[token] = 0;

                SessionBootstrap.Write(
                    LogCategories.TraceTick,
                    "INFO",
                    TraceFormatter.FormatMethod(__originalMethod, __instance, __args, -1, null) +
                    $" callsSinceLast={calls}");
                return;
            }

            var sw = Stopwatches.Value;
            sw.Reset();
            sw.Start();
        }

        public static Exception UniversalFinalizer(Exception __exception, MethodBase __originalMethod, object[] __args, object __instance)
        {
            if (__originalMethod == null || _insideTracer || TraceFormatter.IsFormatting)
            {
                return __exception;
            }

            if (_settings != null && IsUnityTick(__originalMethod) && _settings.ThrottleUnityTickMs > 0)
            {
                return __exception;
            }

            _insideTracer = true;
            try
            {
                long elapsed = -1;
                var sw = Stopwatches.Value;
                if (sw != null && sw.IsRunning)
                {
                    sw.Stop();
                    elapsed = sw.ElapsedMilliseconds;
                }

                SessionBootstrap.Write(
                    LogCategories.Trace,
                    __exception == null ? "INFO" : "ERROR",
                    TraceFormatter.FormatMethod(__originalMethod, __instance, __args, elapsed, __exception));
            }
            catch
            {
                // Never let trace logging break Harmony finalizer chain
            }
            finally
            {
                _insideTracer = false;
            }

            return __exception;
        }
    }
}
