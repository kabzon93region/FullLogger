# Full Logger

**GitHub:** [kabzon93region](https://github.com/kabzon93region)
**BepInEx mod for Escape from Tarkov (SPT / Fika)** — universal exhaustive session logging for debugging mods and the game itself.

Works on **any** game instance: player client, Fika headless host, listen-host, or offline SPT. One zip, same DLL everywhere.

Captures Unity console, BepInEx logs, `LogOutput.log`, BSG `Logs/`, Harmony patch audit, environment snapshot (role, Fika, network), and dynamic method traces (game + all plugins). Log files rotate at **10 MB** per part. Each launch creates a new session folder.

> ⚠️ **Debug tool only.** Large logs, slower startup (dynamic Harmony patches), high disk usage. On headless, patches apply slower (40/frame) to avoid freezes — full capture is still enabled.

## Requirements

| Component | Version |
|-----------|---------|
| **SPT** | 4.0.x (tested on 4.0.13) |
| **BepInEx** | 5.4.x |
| **Fika** | optional (works with or without coop) |

## Install

1. Download the latest release zip `FullLogger_(universal)_vX.Y.Z_*.zip`.
2. Extract into your game root (folder with `EscapeFromTarkov.exe`).
3. Repeat on **each** instance you need to debug (client, headless, etc.).
4. Result: `BepInEx/plugins/FullLogger.dll` (flat, без подпапки `FullLogger/`)

Or build from source (see below).

## Log output

```
BepInEx/FullLogger/sessions/2026-06-23_21-37-45/
  part_00001.log
  part_00002.log
  session_summary.txt
  ...
BepInEx/FullLogger/sessions/latest_session.txt
```

### Log categories

| Category | Source |
|----------|--------|
| `UNITY` | Unity / mirrored console output |
| `BEPINEX` | `BepInEx.Logging.Logger` |
| `HARMONY` | Every `Harmony.Patch` / `Unpatch` |
| `TRACE` | Dynamic method enter/exit with arguments |
| `TRACE_TICK` | Throttled `Update` / `FixedUpdate` / `LateUpdate` |
| `SESSION` / `PLUGIN` / `ENV` | Session start, plugin list, machine/Fika/network snapshot |
| `BEPINEX_FILE` | Live tail `BepInEx/LogOutput.log` (fallback если hook пропустил строку) |
| `GAME_LOG` | Tail of `Logs/**/*.log` |

### Self-contained debugging

A session folder should be enough for **any developer** to diagnose client/headless issues without extra log files. Search order:

1. `session_summary.txt` — ERROR/WARN по модам
2. `ENV` — role, paths, Fika raid code, profile
3. `ERROR` / `BEPINEX` / `BEPINEX_FILE` — failures and mod messages
4. `TRACE` — method flow around the bug (inventory, shooting, spawn, etc.)
5. `PLUGIN` — which mods were loaded

Анализ через утилиту:
```bash
python tools/logs/analyze_logs.py --source client2_fulllogger_latest --filter LIV_FIKA
```

SPT **server** logs (`user/logs`) are still separate — this mod runs inside the EFT process only.

### Verify

In `BepInEx/LogOutput.log` after launch:

```
[FULL_LOGGER] Full Logger v1.0.0 session=...\BepInEx\FullLogger\sessions\...
```

## Configuration

File: `BepInEx/config/com.dematch.fulllogger.cfg`

| Key | Default | Description |
|-----|---------|-------------|
| `General / Enabled` | `true` | Master switch |
| `General / MaxPartSizeMb` | `10` | Rotate log file at this size (MB) |
| `General / SessionRoot` | `BepInEx/FullLogger/sessions` | Session folder (relative to game root or absolute) |
| `Capture / MirrorUnityLog` | `true` | Unity log sink |
| `Capture / MirrorBepInExLog` | `false` | BepInEx `ManualLogSource` hook (heavy with many mods) |
| `Capture / MirrorLogOutputFile` | `true` | Continuous `LogOutput.log` tail (bootstrap + poll/watcher) |
| `Capture / MirrorGameLogs` | `false` | Mirror `Logs/*.log` (BSG game logs) |
| `Capture / LogHarmonyPatchAudit` | `false` | Log all Harmony patches (heavy) |
| `Trace / DynamicTraceGame` | `false` | Trace `Assembly-CSharp` (`EFT`, `Comfort`, `SPT`) |
| `Trace / ThrottleUnityTickMs` | `1000` | Throttle tick methods (`0` = every frame, very heavy) |
| `Trace / TracePropertyAccessors` | `false` | Include `get_` / `set_` (extremely noisy) |
| `Trace / DynamicTraceDelayFrames` | `3` | Wait N frames after load before dynamic trace (min 30 on headless) |
| `Trace / PatchesPerFrame` | `0` | Harmony patches per frame (`0` = auto: 200 client / 40 headless) |

## Build from source

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (builds `netstandard2.1`)
- SPT/EFT install with BepInEx and game DLLs

### Environment

Set your game root (folder containing `BepInEx` and `EscapeFromTarkov_Data`):

**Windows (PowerShell):**

```powershell
$env:EFT_GAME_ROOT = "R:\Games\SPT"
dotnet build FullLogger.csproj -c Release
```

**Or** pass MSBuild property:

```powershell
dotnet build FullLogger.csproj -c Release -p:TarkovDir="R:\Games\SPT\"
```

Output: `bin/Release/FullLogger.dll` → copy to `BepInEx/plugins/FullLogger/`.

### Project pack (maintainers)

From `CURSORAIMODING` workspace:

```bash
python tools/pack/pack_fulllogger.py
```

Creates `releases/FullLogger_v1.0.0_<date>.zip` (game-root layout).

## Tips

- Rename plugin folder to `000-FullLogger` to load earlier and capture more startup logs.
- For shooting/inventory bugs: search `TRACE` for `FirearmController`, `Proceed`, `vmethod_1`, `Throw`.
- Disable `DynamicTraceGame` to reduce size while keeping mod traces.

## License

[MIT](LICENSE)

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## Поддержать проект
Разовый донат картой РФ, СБП, ЮMoney, VK Pay:  
**[DonationAlerts → kabzon93region](https://www.donationalerts.com/r/kabzon93region)**
