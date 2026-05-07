<div align="center">

# Mem Leak Inspector

**Server-side memory diagnostics and profiling for [Vintage Story](https://www.vintagestory.at/).**

Snapshot tracking · instance diffing · in-world heat overlays · thread monitoring · .NET runtime counters · CSV/JSON export · offline dashboard.

[![CI](https://github.com/Elocrypt/MemLeakInspector/actions/workflows/ci.yml/badge.svg)](https://github.com/Elocrypt/MemLeakInspector/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Elocrypt/MemLeakInspector?include_prereleases)](https://github.com/Elocrypt/MemLeakInspector/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![VS 1.22.0](https://img.shields.io/badge/Vintage%20Story-1.22.0-purple)](https://www.vintagestory.at/)

</div>

---

> **2.0.0 is a complete rewrite.** The mod now targets Vintage Story 1.22.0 on .NET 10 with a clean service-oriented architecture. Configuration from 1.x migrates automatically - the flat config is rewritten into the new sectioned format on first load. All `/mem` commands are preserved; several new ones are added.

## Features

<table>
<tr>
<td width="50%" valign="top">

### Memory snapshots
- Capture point-in-time snapshots of every tracked type's live instance count, estimated memory footprint, and per-chunk density
- **Snapshot diffing** - compare any two snapshots to see which types grew, shrank, or appeared/disappeared, sorted by delta
- **Per-instance tracking** - optionally record every entity and block-entity ID and position (TRUE + HUD coords) for detailed investigation
- **Fuzzy file resolution** - reference snapshots by name, prefix, or full filename; the finder searches main and autosnap directories
- **Snapshot versioning** - a `Version` field in the JSON for forward/backward compatibility and future migration

### Heatmaps & highlighting
- **In-world heat overlay** - `/mem showheat` sends chunk-level highlights to all online players, colored by instance growth intensity since the last snapshot
- Per-chunk growth CSV export with TRUE and HUD coordinates for external analysis
- Rate-limited and distance-culled highlight packets to avoid spamming clients on large servers

### Tracking & filtering
- **Allow/deny regex filters** - control exactly which types get tracked, configurable at runtime via `/mem track allow <regex>` and `/mem track deny <regex>`
- **Teleport to instance** - `/mem tp <id>` jumps you to the world position of any tracked entity or block entity by ID or ID prefix

### Export & dashboard
- CSV export for snapshots (type counts), instances (with positions), chunk heatmaps, thread history, and .NET runtime counters (wide time-series format)
- Compressed JSON snapshots (gzip) with configurable retention policies
- **Offline HTML dashboard** - drag-and-drop `.json` or `.csv` files into the browser for sorting, filtering, and charting without any server connection

</td>
<td width="50%" valign="top">

### Watchers & alerts
- **Type watchers** - `/mem watch <type> [interval]` polls a specific type on a cadence and logs whether it's stable, growing, or leaking (>50 delta per interval)
- **Spike detection** - `/mem alertwatch` starts a background watcher that compares successive snapshots and warns when memory or instance-count deltas exceed configurable thresholds
- **Auto-snapshotting** - `/mem autosnap [interval]` takes periodic snapshots to a dedicated subfolder for time-series analysis
- **Live leak watch** - `/mem watchheat [threshold]` monitors all types for rapid growth above a configurable count, logging alerts for anything that spikes

### Thread monitoring
- **Thread snapshots** - `/mem threads` captures a point-in-time view of every OS-level thread: state, wait reason, and cumulative CPU time
- **Background thread logger** - `/mem threadwatch` polls on an interval, builds a rolling history, and exports ASCII thread-count graphs and per-thread CSV on stop
- Thread history JSON dump for offline analysis

### .NET runtime counters
- Live sampling of `System.Runtime` event counters: allocation rate, GC heap size, working set, Gen 0/1/2 collection counts, and more
- `/mem runtime` shows current values and 60-second rolling averages
- `/mem runtimecsv` exports the full counter history as a wide CSV table (one column per counter, one row per second)

### Configuration
- **Sectioned config** - settings split into logical groups: `Snapshots`, `Tracking`, `Alerts`, `Threads`, `Heat`, and `Runtime`, each with sensible defaults
- Auto-normalization clamps out-of-range values on load
- Automatic migration from the 1.x flat config format
- Async commands enabled by default - heavy operations (diffs, reports, heatmaps) run on background threads

</td>
</tr>
</table>

## Install

1. Download the latest `MemLeakInspector_<version>.zip` from the [Releases](https://github.com/Elocrypt/MemLeakInspector/releases) page.
2. Drop the zip (don't extract it) into your server's `Mods/` folder:
   - **Windows:** `%AppData%\VintagestoryData\Mods`
   - **Linux:** `~/.config/VintagestoryData/Mods`
3. Start the server. Use `/mem snap` to take your first snapshot.
4. Optional: Open `dashboard/index.html` in your browser to load and analyze exported files offline.

Server-side only - no client install required. Players see heat-overlay highlights automatically when a `/mem showheat` is triggered.

## Commands

All commands live under `/mem` and require the `controlserver` privilege.

### Snapshots & analysis

| Command | Description |
|---|---|
| `/mem snap [name]` | Take a memory snapshot |
| `/mem list` | List saved snapshot files |
| `/mem diff <A> <B>` | Compare two snapshots by instance-count delta |
| `/mem report <name>` | Show top types in a snapshot |
| `/mem summary [count]` | Average top types across the last N snapshots |
| `/mem memusage <name>` | Estimated memory per type in a snapshot |
| `/mem top [n]` | Top growth since last snapshot |
| `/mem find <regex>` | Filter types in the latest snapshot by regex |
| `/mem export <name>` | Export a snapshot to CSV |
| `/mem snapcsv <name>` | Export instance positions to CSV (TRUE + HUD coords) |
| `/mem graph <type> [count]` | Time-series CSV and ASCII graph for a type |

### Watching & automation

| Command | Description |
|---|---|
| `/mem watch <type> [interval]` | Track a type's growth on a cadence |
| `/mem unwatch <type>` | Stop watching a type |
| `/mem unwatchall` | Stop all watches |
| `/mem autosnap [interval]` | Auto-snapshot every N seconds |
| `/mem autosnapstop` | Stop auto-snapshotting |
| `/mem watchheat [threshold]` | Alert when any type grows ≥ threshold per cycle |
| `/mem watchheatstop` | Stop the leak watcher |
| `/mem alertwatch` | Start memory/instance spike detection |
| `/mem alertstop` | Stop spike detection |

### Heat & highlighting

| Command | Description |
|---|---|
| `/mem showheat` | Highlight leaking chunks in-world |
| `/mem heatmap <A> <B>` | Type-level growth between two snapshots |
| `/mem heatmapexport <A> <B>` | Export type delta to CSV |
| `/mem heatmapcsv <A> <B>` | Export chunk growth to CSV with coordinates |

### Threads & runtime

| Command | Description |
|---|---|
| `/mem threads` | Take a single thread snapshot |
| `/mem threadwatch` | Start background thread logging |
| `/mem threadwatchstop` | Stop logging and export graph |
| `/mem threadexport` | Export thread history to CSV |
| `/mem threaddump` | Export thread history to JSON |
| `/mem runtime` | Show .NET runtime counters |
| `/mem runtimecsv [name]` | Export counter history to wide CSV |

### Tracking

| Command | Description |
|---|---|
| `/mem track allow <regex>` | Add an allow pattern |
| `/mem track deny <regex>` | Add a deny pattern |
| `/mem track show` | Show current allow/deny lists |
| `/mem tp <id>` | Teleport to a tracked instance |

## Configuration

Settings are stored at `ModConfig/MemLeakInspectorConfig.json` and split into sections:

| Section | Key settings |
|---|---|
| `Snapshots` | `CompressSnapshots`, `MaxSnapshotsOnDisk`, `ForceFullGcBeforeSnapshot`, `DiffPreviewLines` |
| `Tracking` | `TrackIndividualEntities`, `AllowListRegex[]`, `DenyListRegex[]` |
| `Alerts` | `MemorySpikeMB`, `InstanceSpike`, `CheckIntervalSec`, `IgnoreSpikeTypeFragments[]` |
| `Threads` | `AutoStart`, `IntervalSec`, `ExcludeSleepingThreads`, `MaxHistory` |
| `Heat` | `Enabled`, `CooldownSec`, `MaxDistance`, `TopChunks` |
| `Runtime` | `Enabled`, `IntervalSec` |
| *(global)* | `EnableAsyncCommands`, `ReportFilterMB` |

Edit while the server is running and reload with a restart, or use the `/mem track` commands to update filters at runtime.

## Compatibility

- **Vintage Story 1.22.0** or later on .NET 10. The [1.x line](https://github.com/Elocrypt/MemLeakInspector/releases) runs on earlier versions.
- Server-side only. Clients receive heat-overlay highlights via the standard `HighlightBlocks` API - no client mod needed.
- Uses Harmony to hook `Entity.Initialize`, `Entity.OnEntityDespawn`, and `BlockEntity.Initialize`. Patches are applied once and cleanly removed on dispose. The mod functions without Harmony (via `AutoTrackedBE` and periodic entity polling) if another mod conflicts.
- No known conflicts with other server-side profiling or diagnostics mods.

---

<details>
<summary><b>Building from source</b></summary>

### Requirements

- Vintage Story 1.22.0 or later (for the referenced game DLLs)
- .NET 10 SDK

### Setup

1. Install the Vintage Story server at a known location.
2. Set environment variables pointing at your install:
   - `VINTAGE_STORY` - the game install directory (contains `Vintagestory.exe` or the server DLLs).
   - `VINTAGE_STORY_DATA` - the data directory (contains `Mods/`, `ModConfig/`, `Saves/`).

   ```powershell
   # Windows (PowerShell)
   [Environment]::SetEnvironmentVariable("VINTAGE_STORY",      "F:\VintageStory\Client_v1.22.0\Vintagestory",   "User")
   [Environment]::SetEnvironmentVariable("VINTAGE_STORY_DATA", "F:\VintageStory\Client_v1.22.0\Vintagestory\v", "User")
   ```

3. Restart your IDE so it picks up the new variables.
4. Open `MemLeakInspector.sln`.

If the variables are not set, `Directory.Build.props` falls back to `F:\VintageStory\Client_v1.22.1\Vintagestory` on Windows.

### Build

```powershell
dotnet build MemLeakInspector.sln -c Release
```

Build output at `src/MemLeakInspector/bin/Release/net10.0/` is a complete, loadable mod folder. By default the build also deploys to `$(VINTAGE_STORY_DATA)\Mods\MemLeakInspector`. Disable with `/p:DeployMod=false`.

### Test

```powershell
dotnet test MemLeakInspector.sln -c Release
```

47 tests covering the tracker, snapshot diffing, store round-trips, file finder, config normalization, size estimation, and utilities.

### Package a release

```powershell
./build/package.ps1 -Configuration Release -Version 2.0.0
```

Produces `build/dist/MemLeakInspector_2.0.0.zip`. On push of a tag matching `v*.*.*`, GitHub Actions runs the same script and publishes a release automatically.

### Architecture

The codebase is organized into clean subsystem folders:

- **`Configuration/`** - root config + per-feature option classes (`SnapshotOptions`, `TrackingOptions`, `AlertOptions`, `ThreadOptions`, `HeatOptions`, `RuntimeOptions`).
- **`Core/`** - slim `ModSystem` entry point, client system stub, `AutoTrackedBE`.
- **`Tracking/`** - `InstanceTracker` (weak-ref management, amortized sweep), `TrackingFilter`, `SizeEstimator`, `InstanceInfo`.
- **`Snapshots/`** - `SnapshotService`, `SnapshotStore` (load/save/compress), `SnapshotDiff`, `SnapshotFinder`, `SnapshotCsv`.
- **`Diagnostics/`** - `ThreadMonitor`, `AlertWatcher`, `RuntimeCounterListener`.
- **`Harmony/`** - centralized `HarmonyManager` + `EntityPatches`.
- **`Rendering/`** - `HeatHighlighter`, `HighlightPacket`.
- **`Commands/`** - `CommandRouter` + one handler class per feature area.
- **`Utils/`** - `Coords`, `AsciiGraph`, `SafeFileName`.

The `ModSystem` is deliberately thin (~130 lines): it loads config, creates services, wires them together, and tears them down on dispose. All business logic lives in the subsystem classes.

</details>

## License

MIT - see [LICENSE](LICENSE).

## Credits

- Created by [Elocrypt](https://github.com/Elocrypt).
