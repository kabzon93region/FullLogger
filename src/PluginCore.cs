using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using FullLogger.Hooks;
using FullLogger.Logging;
using FullLogger.Tracing;
using HarmonyLib;
using UnityEngine;

namespace FullLogger
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.NAME, PluginInfo.VERSION)]
    public sealed class PluginCore : BaseUnityPlugin
    {
        internal static PluginCore Instance { get; private set; }

        private Harmony _harmony;
        private Coroutine _dynamicTraceCoroutine;
        private Coroutine _runtimePollCoroutine;
        private LogOutputMirror _logOutputMirror;
        private GameLogMirror _gameLogMirror;

        internal ConfigEntry<bool> Enabled;
        internal ConfigEntry<string> SessionRoot;
        internal ConfigEntry<int> MaxPartSizeMb;
        internal ConfigEntry<bool> MirrorUnityLog;
        internal ConfigEntry<bool> MirrorBepInExLog;
        internal ConfigEntry<bool> MirrorLogOutputFile;
        internal ConfigEntry<bool> MirrorGameLogs;
        internal ConfigEntry<bool> LogHarmonyPatchAudit;
        internal ConfigEntry<bool> DynamicTraceMods;
        internal ConfigEntry<bool> DynamicTraceGame;
        internal ConfigEntry<bool> TraceConstructors;
        internal ConfigEntry<bool> TracePropertyAccessors;
        internal ConfigEntry<int> ThrottleUnityTickMs;
        internal ConfigEntry<string> GameNamespacePrefixes;
        internal ConfigEntry<int> DynamicTraceDelayFrames;
        internal ConfigEntry<int> PatchesPerFrame;
        internal ConfigEntry<bool> BackgroundWrite;
        internal ConfigEntry<int> MaxPendingLogLines;
        internal ConfigEntry<int> RuntimePollIntervalSeconds;
        internal ConfigEntry<int> TailPollIntervalMs;
        internal ConfigEntry<bool> TraceInventoryOps;
        internal ConfigEntry<bool> TraceCombatRicochet;

        private BackgroundLogTailer _backgroundTailer;
        private void Awake()
        {
            Instance = this;
            BindConfig();

            if (!Enabled.Value)
            {
                Logger.LogWarning("[FULL_LOGGER] Disabled in config");
                return;
            }

            var root = ResolveSessionRoot(SessionRoot.Value);
            SessionBootstrap.InitializeEarly(
                root,
                MaxPartSizeMb.Value,
                BackgroundWrite.Value,
                MaxPendingLogLines.Value);
            EnvironmentSnapshot.WriteStartupSnapshot(this);

            _backgroundTailer = BackgroundLogTailer.Start(Mathf.Max(50, TailPollIntervalMs.Value));

            if (MirrorUnityLog.Value)
            {
                UnityLogSink.Install();
            }

            if (MirrorLogOutputFile.Value)
            {
                _logOutputMirror = new LogOutputMirror();
                _logOutputMirror.Start();
            }

            if (MirrorGameLogs.Value)
            {
                _gameLogMirror = new GameLogMirror();
                _gameLogMirror.Start();
            }

            _harmony = new Harmony(PluginInfo.GUID);

            if (MirrorBepInExLog.Value)
            {
                try
                {
                    _harmony.PatchAll(typeof(Patches.BepInExLogPatches));
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[FULL_LOGGER] BepInEx log hook skipped: {ex.Message}");
                }
            }

            if (LogHarmonyPatchAudit.Value)
            {
                _harmony.PatchAll(typeof(Patches.HarmonyAuditPatches));
            }

            if (TraceInventoryOps.Value || TraceCombatRicochet.Value)
            {
                GameEventTracer.TryApply(_harmony, Logger, TraceInventoryOps.Value, TraceCombatRicochet.Value);
            }

            _dynamicTraceCoroutine = StartCoroutine(DeferredDynamicTrace());
            _runtimePollCoroutine = StartCoroutine(RuntimeEnvironmentPoll());

            var role = FullLoggerEnvironment.IsHeadlessOrDedicatedServer() ? "headless_host" : "client";
            Logger.LogInfo(
                $"[FULL_LOGGER] {PluginInfo.NAME} v{PluginInfo.VERSION} role={role} session={SessionFileLogger.Instance?.SessionDirectory}");
            SessionBootstrap.Write(
                LogCategories.Session,
                "INFO",
                $"{PluginInfo.NAME} v{PluginInfo.VERSION} initialized (role={role}, universal debug logging)");
        }

        private void OnDestroy()
        {
            if (_dynamicTraceCoroutine != null)
            {
                StopCoroutine(_dynamicTraceCoroutine);
            }

            if (_runtimePollCoroutine != null)
            {
                StopCoroutine(_runtimePollCoroutine);
            }

            UnityLogSink.Uninstall();
            _logOutputMirror?.Dispose();
            _gameLogMirror?.Dispose();
            _backgroundTailer?.Dispose();
            _harmony?.UnpatchSelf();
            SessionFileLogger.Instance?.Dispose();
        }

        private void OnApplicationQuit()
        {
            SessionFileLogger.Instance?.Flush();
        }

        private void BindConfig()
        {
            Enabled = Config.Bind("General", "Enabled", true, "Включить Full Logger.");
            SessionRoot = Config.Bind(
                "General",
                "SessionRoot",
                "BepInEx/FullLogger/sessions",
                "Папка сессий относительно корня игры (или абсолютный путь).");
            MaxPartSizeMb = Config.Bind("General", "MaxPartSizeMb", 10,
                "Максимальный размер одного файла лога (МБ), затем ротация.");
            BackgroundWrite = Config.Bind("General", "BackgroundWrite", true,
                "Запись логов в отдельном потоке (не блокирует игру). Рекомендуется всегда включено.");
            MaxPendingLogLines = Config.Bind("General", "MaxPendingLogLines", 65536,
                "Размер очереди фоновой записи. При переполнении старые строки отбрасываются.");
            RuntimePollIntervalSeconds = Config.Bind("General", "RuntimePollIntervalSeconds", 30,
                "Интервал снимка Fika/профиля (сек). 0 = только при старте. Не читает файлы логов.");
            TailPollIntervalMs = Config.Bind("General", "TailPollIntervalMs", 250,
                "Интервал фонового tail BepInEx/LogOutput и Logs/*.log (мс). На главном потоке не работает.");

            MirrorUnityLog = Config.Bind("Capture", "MirrorUnityLog", false,
                "Зеркалировать Unity/BepInEx LogOutput в файлы сессии.");
            MirrorBepInExLog = Config.Bind("Capture", "MirrorBepInExLog", false,
                "Перехватывать ManualLogSource.Log (BepInEx плагины). Тяжело при 50+ модах — включайте только для точечной отладки.");
            MirrorLogOutputFile = Config.Bind("Capture", "MirrorLogOutputFile", true,
                "Фоновый tail BepInEx/LogOutput.log (поток FullLogger-Tailer, без нагрузки на FPS).");
            MirrorGameLogs = Config.Bind("Capture", "MirrorGameLogs", true,
                "Фоновый tail Logs/*.log (игровые логи BSG, краши/исключения).");
            LogHarmonyPatchAudit = Config.Bind("Capture", "LogHarmonyPatchAudit", false,
                "Логировать все Harmony.Patch / Unpatch (не во время массовой трассировки).");

            DynamicTraceMods = Config.Bind("Trace", "DynamicTraceMods", false,
                "ОПАСНО: Harmony-патч всех методов модов. Только для точечной отладки. Перезапуск игры.");
            DynamicTraceGame = Config.Bind("Trace", "DynamicTraceGame", false,
                "ОПАСНО: Harmony-патч методов Assembly-CSharp. Только для точечной отладки. Перезапуск игры.");
            TraceConstructors = Config.Bind("Trace", "TraceConstructors", false,
                "Включать конструкторы в динамическую трассировку (только если DynamicTrace* включён).");
            TracePropertyAccessors = Config.Bind("Trace", "TracePropertyAccessors", false,
                "Включать get_/set_ (очень шумно).");
            ThrottleUnityTickMs = Config.Bind("Trace", "ThrottleUnityTickMs", 1000,
                "Троттлинг Update/FixedUpdate/LateUpdate (мс), 0 = без троттлинга.");
            GameNamespacePrefixes = Config.Bind(
                "Trace",
                "GameNamespacePrefixes",
                "EFT,Comfort,SPT",
                "Префиксы namespace для Assembly-CSharp через запятую.");
            DynamicTraceDelayFrames = Config.Bind("Trace", "DynamicTraceDelayFrames", 3,
                "Сколько кадров ждать после загрузки плагинов перед динамической трассировкой.");
            PatchesPerFrame = Config.Bind("Trace", "PatchesPerFrame", 0,
                "Сколько Harmony-патчей применять за кадр (0 = авто: 200 client / 40 headless).");

            TraceInventoryOps = Config.Bind("GameEvents", "TraceInventoryOps", true,
                "Логировать Fika inventory operations (move/throw/discard) в GAME_EVENT.");
            TraceCombatRicochet = Config.Bind("GameEvents", "TraceCombatRicochet", true,
                "Логировать рикошеты (EffectsCommutator) в GAME_EVENT.");
        }

        private static string ResolveSessionRoot(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = "BepInEx/FullLogger/sessions";
            }

            if (Path.IsPathRooted(configured))
            {
                return configured;
            }

            return Path.GetFullPath(Path.Combine(BepInEx.Paths.PluginPath, "..", "..", configured.Replace('/', Path.DirectorySeparatorChar)));
        }

        private IEnumerator DeferredDynamicTrace()
        {
            var isHeadless = FullLoggerEnvironment.IsHeadlessOrDedicatedServer();
            var frames = Mathf.Max(0, DynamicTraceDelayFrames.Value);
            if (isHeadless)
            {
                frames = Mathf.Max(frames, 30);
            }

            for (var i = 0; i < frames; i++)
            {
                yield return null;
            }

            SessionBootstrap.WritePluginInventory();

            if (!DynamicTraceMods.Value && !DynamicTraceGame.Value)
            {
                SessionBootstrap.Write(LogCategories.Trace, "INFO",
                    "Dynamic tracing OFF (capture-only mode). Enable DynamicTraceMods/Game only for deep debug.");
                yield break;
            }

            SessionBootstrap.Write(LogCategories.Trace, "WARN",
                "Dynamic tracing ENABLED — expect severe lag. Disable after debugging.");

            var settings = new DynamicTraceSettings
            {
                TraceModAssemblies = DynamicTraceMods.Value,
                TraceGameNamespaces = DynamicTraceGame.Value,
                TraceConstructors = TraceConstructors.Value,
                TracePropertyAccessors = TracePropertyAccessors.Value,
                ThrottleUnityTickMs = Mathf.Max(0, ThrottleUnityTickMs.Value),
                GameNamespacePrefixes = GameNamespacePrefixes.Value
            };

            var patchesPerFrame = PatchesPerFrame.Value > 0
                ? PatchesPerFrame.Value
                : (isHeadless ? 40 : 200);

            SessionBootstrap.Write(
                LogCategories.Trace,
                "INFO",
                $"Dynamic trace starting: patchesPerFrame={patchesPerFrame}, headless={isHeadless}");

            yield return DynamicMethodTracer.ApplyAsync(_harmony, settings, patchesPerFrame);
        }

        private IEnumerator RuntimeEnvironmentPoll()
        {
            var interval = Mathf.Max(0, RuntimePollIntervalSeconds.Value);

            while (!FikaSessionGuard.IsTarkovApplicationReady())
            {
                yield return new WaitForSeconds(interval > 0 ? interval : 10f);
            }

            if (interval <= 0)
            {
                EnvironmentSnapshot.WritePeriodicUpdateIfChanged();
                yield break;
            }

            while (true)
            {
                yield return new WaitForSeconds(interval);
                EnvironmentSnapshot.WritePeriodicUpdateIfChanged();
            }
        }
    }
}
