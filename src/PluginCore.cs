using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using FullLogger.Hooks;
using FullLogger.Logging;
using FullLogger.Tracing;
using FullLogger.Monitoring;
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
        private GameObject _monitoringRoot;
        private FullLoggerCaptureController _captureController;

        internal FullLoggerCaptureController CaptureController => _captureController;
        internal BackgroundLogTailer BackgroundTailer => _backgroundTailer;
        internal GameObject MonitoringRoot => _monitoringRoot;

        internal ConfigEntry<bool> Enabled;
        internal ConfigEntry<string> SessionRoot;
        internal ConfigEntry<int> MaxPartSizeMb;
        internal ConfigEntry<bool> MirrorUnityLog;
        internal ConfigEntry<bool> MirrorBepInExLog;
        internal ConfigEntry<bool> MirrorLogOutputFile;
        internal ConfigEntry<int> LogOutputCaptureMode;
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
        internal ConfigEntry<bool> MetabolismWatchdogEnabled;
        internal ConfigEntry<float> MetabolismWatchIntervalSec;
        internal ConfigEntry<float> MetabolismStuckThresholdSec;
        internal ConfigEntry<bool> MetabolismAutoResync;
        internal ConfigEntry<int> MetabolismMaxResyncAttempts;
        internal ConfigEntry<bool> MetabolismVerboseLogging;
        internal ConfigEntry<float> FikaNetLogIntervalSec;
        internal ConfigEntry<bool> RaidMonitoringEnabled;
        internal ConfigEntry<bool> RaidSnapshotEnabled;
        internal ConfigEntry<float> RaidSnapshotIntervalSec;
        internal ConfigEntry<bool> RaidIncludeHealth;
        internal ConfigEntry<bool> FikaNetLogDuringRaid;
        internal ConfigEntry<float> GameLogRetryIntervalSec;
        internal ConfigEntry<bool> TraceExtractEvents;

        internal GameLogMirror GameLogMirror => _gameLogMirror;

        internal void EnsureLogOutputMirror()
        {
            if (_logOutputMirror != null)
            {
                return;
            }

            _logOutputMirror = new LogOutputMirror();
            _logOutputMirror.Start();
        }

        internal void StopLogOutputMirror()
        {
            if (_logOutputMirror == null)
            {
                return;
            }

            _logOutputMirror.Dispose();
            _logOutputMirror = null;
        }

        internal void EnsureGameLogMirror()
        {
            if (_gameLogMirror != null)
            {
                return;
            }

            _gameLogMirror = new GameLogMirror();
            _gameLogMirror.Start();
        }

        internal void StopGameLogMirror()
        {
            if (_gameLogMirror == null)
            {
                return;
            }

            _gameLogMirror.Dispose();
            _gameLogMirror = null;
        }

        private MetabolismWatchdogBehaviour _metabolismWatchdog;
        private RaidSessionMonitorBehaviour _raidMonitor;
        private float _fikaNetLogTimer;
        private BackgroundLogTailer _backgroundTailer;

        private void Awake()
        {
            Instance = this;
            BindConfig();
            _captureController = new FullLoggerCaptureController(this, Logger);
            _captureController.BindSettingChangedEvents();

            if (!Enabled.Value)
            {
                Logger.LogWarning("[FULL_LOGGER] Disabled in config (General.Enabled=false). Set true and restart to enable.");
                return;
            }

            StartCaptureSystems();
        }

        private void StartCaptureSystems()
        {
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

            if (TraceExtractEvents.Value)
            {
                RaidEventTracer.TryApply(_harmony, Logger);
            }

            _dynamicTraceCoroutine = StartCoroutine(DeferredDynamicTrace());
            _runtimePollCoroutine = StartCoroutine(RuntimeEnvironmentPoll());

            _monitoringRoot = new GameObject("FullLogger_Monitoring");
            DontDestroyOnLoad(_monitoringRoot);
            _monitoringRoot.hideFlags = HideFlags.HideAndDontSave;
            _metabolismWatchdog = _monitoringRoot.AddComponent<MetabolismWatchdogBehaviour>();
            _metabolismWatchdog.Initialize(this);
            _raidMonitor = _monitoringRoot.AddComponent<RaidSessionMonitorBehaviour>();
            _raidMonitor.Initialize(this);

            _captureController.MarkInitialized();
            _captureController.ApplyFromConfig("startup");

            var role = FullLoggerEnvironment.IsHeadlessOrDedicatedServer() ? "headless_host" : "client";
            Logger.LogInfo(
                $"[FULL_LOGGER] {PluginInfo.NAME} v{PluginInfo.VERSION} role={role} session={SessionFileLogger.Instance?.SessionDirectory}");
            SessionBootstrap.Write(
                LogCategories.Session,
                "INFO",
                $"{PluginInfo.NAME} v{PluginInfo.VERSION} initialized (role={role}). " +
                "General.Enabled / Mirror* / intervals apply live; Harmony patches need restart.");
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
            StopLogOutputMirror();
            StopGameLogMirror();
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
            RuntimePollIntervalSeconds = Config.Bind("General", "RuntimePollIntervalSeconds", 15,
                "Интервал снимка Fika/профиля (сек). 0 = только при старте. Не читает файлы логов.");
            TailPollIntervalMs = Config.Bind("General", "TailPollIntervalMs", 1000,
                "Интервал фонового tail BepInEx/LogOutput и Logs/*.log (мс). 250=тяжело на больших LogOutput. Применяется на лету.");

            MirrorUnityLog = Config.Bind("Capture", "MirrorUnityLog", false,
                "Зеркалировать Unity/BepInEx LogOutput в файлы сессии.");
            MirrorBepInExLog = Config.Bind("Capture", "MirrorBepInExLog", false,
                "Перехватывать ManualLogSource.Log (BepInEx плагины). Тяжело при 50+ модах — включайте только для точечной отладки.");
            MirrorLogOutputFile = Config.Bind("Capture", "MirrorLogOutputFile", true,
                "Фоновый tail BepInEx/LogOutput.log. Выкл/режим фильтра — на лету (General.Enabled=false = полная остановка).");
            LogOutputCaptureMode = Config.Bind("Capture", "LogOutputCaptureMode", 2,
                "Фильтр LogOutput: 0=Off 1=ErrorsOnly 2=ErrorsAndKeywords 3=All. 2 по умолчанию — без BOT_RECONCILE спама. На лету.");
            MirrorGameLogs = Config.Bind("Capture", "MirrorGameLogs", false,
                "Фоновый tail Logs/*.log (игровые логи BSG). На лету.");
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

            TraceInventoryOps = Config.Bind("GameEvents", "TraceInventoryOps", false,
                "Harmony: Fika inventory ops в GAME_EVENT. Вкл/выкл postfix на лету; патч остаётся до рестарта.");
            TraceCombatRicochet = Config.Bind("GameEvents", "TraceCombatRicochet", false,
                "Harmony: рикошеты в GAME_EVENT. Вкл/выкл prefix на лету; патч остаётся до рестарта.");

            MetabolismWatchdogEnabled = Config.Bind("Metabolism", "WatchdogEnabled", true,
                "Детект зависшей еды/воды (METABOLISM_STUCK) в рейде.");
            MetabolismWatchIntervalSec = Config.Bind("Metabolism", "WatchIntervalSec", 5f,
                "Интервал проверки metabolism (сек).");
            MetabolismStuckThresholdSec = Config.Bind("Metabolism", "StuckThresholdSec", 20f,
                "Секунд на max без изменений/rates=0 перед METABOLISM_STUCK.");
            MetabolismAutoResync = Config.Bind("Metabolism", "AutoResync", true,
                "Пробовать re-enable metabolism и probe ChangeEnergy/Hydration.");
            MetabolismMaxResyncAttempts = Config.Bind("Metabolism", "MaxResyncAttempts", 5,
                "Максимум auto-resync за один эпизод stuck.");
            MetabolismVerboseLogging = Config.Bind("Metabolism", "VerboseLogging", false,
                "Логировать каждый sample metabolism (шумно).");
            FikaNetLogIntervalSec = Config.Bind("FikaNet", "LogIntervalSec", 10f,
                "Периодически писать RTT/packet loss в FIKA_NET (0 = только в RAID snapshot).");

            RaidMonitoringEnabled = Config.Bind("Raid", "MonitoringEnabled", true,
                "In-raid мониторинг: RAID_START/END, периодические снимки, retry BSG Logs.");
            RaidSnapshotEnabled = Config.Bind("Raid", "SnapshotEnabled", true,
                "Писать [RAID_SNAPSHOT] каждые N секунд в рейде (лёгкая нагрузка, ~1–3% FPS).");
            RaidSnapshotIntervalSec = Config.Bind("Raid", "SnapshotIntervalSec", 30f,
                "Интервал RAID snapshot (сек). Минимум 3. На лету.");
            RaidIncludeHealth = Config.Bind("Raid", "IncludeHealth", true,
                "В snapshot включать energy/hydration/rates (metabolism).");
            FikaNetLogDuringRaid = Config.Bind("Raid", "FikaNetDuringRaid", true,
                "Дублировать FIKA_NET в каждый RAID snapshot.");
            GameLogRetryIntervalSec = Config.Bind("Raid", "GameLogRetryIntervalSec", 30f,
                "Как часто искать папку BSG Logs/ и подключать tail (сек).");
            TraceExtractEvents = Config.Bind("Raid", "TraceExtractEvents", false,
                "Harmony: лог F8/extract (EXTRACT). Патч при старте; логирование вкл/выкл на лету.");
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
                TickFikaNetLogging(interval);
            }
        }

        private void TickFikaNetLogging(float intervalSec)
        {
            var logInterval = Mathf.Max(0f, FikaNetLogIntervalSec.Value);
            if (logInterval <= 0f)
            {
                return;
            }

            _fikaNetLogTimer += intervalSec;
            if (_fikaNetLogTimer < logInterval)
            {
                return;
            }

            _fikaNetLogTimer = 0f;
            FikaNetworkStatsMonitor.WritePeriodicIfInRaid(force: RaidStateDetector.IsInRaidQuick());
        }
    }
}
