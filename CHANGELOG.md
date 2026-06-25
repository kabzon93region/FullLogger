## 1.2.2 — 2026-06-25

- **Pack/deploy**: flat layout `BepInEx/plugins/FullLogger.dll` (без `plugins/FullLogger/`)
- При деплое удаляется устаревшая папка `BepInEx/plugins/FullLogger/`

## 1.2.1 — 2026-06-25

- Дефолты конфига для «удалил cfg → первый запуск»: `MirrorGameLogs=true`, безопасный capture-only профиль
- `TraceConstructors=false` по умолчанию (DynamicTrace выключен)

## 1.2.0 — 2026-06-12

- **Fix FPS micro-stutter каждые ~2 с**: убран `Poll()` LogOutput/GameLogs с главного потока (корутина `WaitForSeconds(2)`)
- **BackgroundLogTailer**: чтение `LogOutput.log` и `Logs/*.log` в потоке `FullLogger-Tailer` (по умолчанию 250 мс)
- **RuntimePollIntervalSeconds** (по умолчанию 30): только снимок Fika/профиля, без file I/O
- **GameEvents**: `TraceInventoryOps` (Fika `RunClientOperation`), `TraceCombatRicochet` (`EffectsCommutator`)
- Категория лога `GAME_EVENT` для точечных игровых событий без DynamicTrace

## 1.1.8 — 2026-06-12

- **LogOutput**: непрерывный tail (watcher + poll), не bootstrap-only при `MirrorBepInExLog`
- **Дедуп**: `LogCaptureCoordinator` — hook `BEPINEX` vs tail `BEPINEX_FILE`, без двойных строк
- **BepInEx hook**: один патч `Log(LogLevel, object)` (корректно для BepInEx 5.4)
- **session_summary.txt**: ERROR/WARN по модам в конце сессии
- **latest_session.txt**: указатель на последнюю папку сессии
- **analyze_logs.py**: источники `client1_fulllogger`, `client2_fulllogger`, `*_latest`

## 1.1.7 — 2026-06-24

- **Fix:** не вызывать `FikaBackendUtils.Profile` / `FikaGlobals.GetProfile` до готовности `TarkovApplication`
- Убраны ошибки `[FikaGlobals] GetProfile: Session was null!` от FullLogger на headless
- Runtime snapshot профиля — через `session.Profile`, periodic poll ждёт сессию

## 1.1.6 — 2026-06-12

- **Fix:** HarmonyX warnings `Profile.Id/Info` и `System.String.Level` при runtime snapshot профиля
- Чтение полей `EFT.Profile` через обычный reflection (без `AccessTools.Property`, который спамит варнинги)
- `ProfileId` + цепочка `Info.Level` через object, а не через `ToString()` строки
- `headless_mods_rules`: FullLogger в `strip_name_patterns` (debug-мод, не нужен на headless)

## 1.1.5 — 2026-06-12

- BepInEx display name: **Full Logger (universal)**

## 1.1.4 — 2026-06-12

- **Фоновая запись логов** (`BackgroundWrite=true`) — диск I/O в потоке `FullLogger-Writer`, игра не ждёт flush
- Очередь до 65536 строк (`MaxPendingLogLines`), при переполнении — drop с предупреждением
- Unity sink уже использует `logMessageReceivedThreaded` — теперь безопасно с фоновым writer

## 1.1.3 — 2026-06-12

- **Fix:** зависание/FPS 1–2 — динамический трейс **выкл по умолчанию** (mods + game)
- LogOutput mirror: bootstrap-only при активном BepInEx hook (нет дубля каждой строки)
- Защита BepInEx hook от рекурсии и собственных логов Full Logger
- Режим по умолчанию: **capture-only** (зеркалирование логов без Harmony на каждый метод)

## 1.1.2 — 2026-06-12

- **Fix:** не патчить `UnityEngine.*`, generic-типы (`ComponentSystem\`2`, `CommonClientApplication\`1`), Unity lifecycle
- Только `DeclaredOnly` методы — не наследуемые от MonoBehaviour/Component
- `UniversalFinalizer` в try/catch + `ThreadStatic` — не ломает Harmony-цепочку
- На headless `DynamicTraceGame` по умолчанию **выкл** (моды BepInEx всё ещё трейсятся)

## 1.1.1 — 2026-06-12

- **Fix:** StackOverflow при старте — рекурсия `UniversalFinalizer` → `FormatMethod` → `ToString()`
- Безопасное форматирование аргументов (`Type#HashCode` вместо `ToString()`)
- Исключены из патчинга: `ToString`, `GetHashCode`, `Equals`, `GetType` и типы `System.*` / `FullLogger.*`
- `ThreadStatic`-защита от повторного входа в трейсер при форматировании

## 1.1.0 — 2026-06-23

- **Universal** deployment: `(universal)` — client, headless, offline; один zip для всех
- Убрано автоотключение на Fika Headless — полный захват логов везде
- Headless: медленнее применяются Harmony-патчи (40/кадр, задержка ≥30 кадров) без потери функций
- `PluginInfo.NAME` без привязки к headless_client
- Конфиг `Trace / PatchesPerFrame` (0 = авто)

## 1.0.2 — 2026-06-23

- Расширенный снимок окружения (`ENV`): роль, IP, пути, Fika RaidCode/профиль
- Зеркалирование `BepInEx/LogOutput.log` (`BEPINEX_FILE`) и `Logs/**/*.log` (`GAME_LOG`)
- Перехват обоих overload `ManualLogSource.Log`
- Периодическое обновление Fika/профиля каждые 5 с
- Метка развёртывания в имени: `(headless_client)`

## v1.0.0 (2026-06-23)

- Первый публичный релиз
- Сессионные логи в `BepInEx/FullLogger/sessions/<дата-время>/`
- Ротация файлов по 10 МБ (`part_00001.log`, `part_00002.log`, …)
- Зеркалирование Unity `logMessageReceivedThreaded`
- Перехват `BepInEx.Logging.Logger.Log`
- Аудит `Harmony.Patch` / `Harmony.Unpatch`
- Динамическая трассировка всех методов DLL из `BepInEx/plugins`
- Динамическая трассировка `Assembly-CSharp` (namespace `EFT`, `Comfort`, `SPT`)
- Троттлинг `Update` / `FixedUpdate` / `LateUpdate` (по умолчанию 1 с)
- Конфиг BepInEx: `com.dematch.fulllogger.cfg`
