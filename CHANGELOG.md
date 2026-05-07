# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-05-07

Complete rewrite of the mod on a new architecture. Feature parity with 1.x is preserved; internal structure is not. Configuration migrates automatically - the flat 1.x config is rewritten into the new sectioned format on first load.

### Added

- **Sectioned configuration.** Settings split into logical groups (`Snapshots`, `Tracking`, `Alerts`, `Threads`, `Heat`, `Runtime`) with per-section option classes. Root config aggregates all sections and auto-normalizes values (clamping out-of-range settings, deduplicating ignore lists) on load.
- **SizeEstimator.** New dedicated type-size estimation system using reflection-based field analysis, one-level base-class walking, struct support, and per-type caching. Replaces the old flat `~96 bytes per instance` heuristic.
- **Amortized tracker sweep.** `InstanceTracker.SweepBatch()` prunes 8 type buckets per call on a 5-second game tick, reclaiming dead weak references and removing empty keys without blocking the main thread.
- **SnapshotService.** Centralized service owning the snapshot directory, semaphore gate, build/save pipeline, and `LastSnapshot` reference. Replaces snapshot logic that was previously inlined in the ModSystem.
- **Snapshot versioning.** `MemSnapshot.Version = 2` field for forward/backward compatibility and future schema migration.
- **SnapshotFinder.** Dedicated fuzzy file resolver - searches by exact name, extension probing (`.json`, `.json.gz`), base-name match, and prefix match across main and autosnap directories. Replaces ad-hoc path construction scattered through old command handlers.
- **Command architecture.** All `/mem` commands organized into five handler classes (`SnapshotCommands`, `WatcherCommands`, `DiagnosticCommands`, `HeatCommands`, `TrackingCommands`) wired through a `CommandRouter`. Each handler receives only the services it needs via constructor injection.
- **New commands:** `/mem top [n]` (top growth since last snapshot), `/mem find <regex>` (filter types by regex), `/mem memusage <name>` (estimated memory per type), `/mem snapcsv <name>` (instance positions to CSV), `/mem threadexport` (thread history to CSV), `/mem threaddump` (thread history to JSON), `/mem runtimecsv [name]` (wide counter CSV), `/mem watchheat [threshold]` and `/mem watchheatstop` (live leak detection), `/mem alertwatch` and `/mem alertstop` (spike detection), `/mem heatmapcsv <A> <B>` (chunk growth to CSV with TRUE and HUD coordinates).
- **Runtime counter integration in snapshots.** Allocation rate and working-set values from `RuntimeCounterListener` are now captured in every snapshot's `GcInfo` block.
- **ThreadMonitor export methods.** `ExportCsv()`, `ExportJson()`, `GenerateGraph()`, and `SaveGraph()` are self-contained on the monitor instead of inlined in command handlers.
- **HarmonyManager.** Centralized class owning the single Harmony instance. Applies all patches exactly once, cleanly removes them on dispose, and logs success or failure. Double-patch prevention is built in.
- **Unit test suite.** 47 tests across 8 test files covering `TrackingFilter`, `SnapshotDiff`, `SizeEstimator`, `SnapshotStore` round-trips, `SnapshotFinder` fuzzy matching, `InstanceTracker` core logic (registration, deduplication, pruning, filtering, sweep), `AsciiGraph`, `SafeFileName`, and config normalization.
- **Improved dashboard.** Redesigned HTML/CSS with a filter input, sort dropdown, sticky table headers, and a Chart.js thread-count line graph. Proper CSV parser handles quoted fields. Row display capped at 5,000 for large files.

### Changed

- **ModSystem is now a thin shell.** Reduced from ~2,100 lines to ~130. It loads config, creates services, wires them together, and tears them down on dispose. All business logic lives in dedicated subsystem classes.
- **InstanceTracker is instance-based**, not static. Harmony patches reach the active instance via `InstanceTracker.Current`, which the ModSystem sets on startup and nulls on dispose. Makes lifetime management explicit and testing possible.
- **Project structure reorganized** from a flat `src/MemLeakInspector/*.cs` layout into eight subsystem folders: `Configuration/`, `Core/`, `Tracking/`, `Snapshots/`, `Diagnostics/`, `Harmony/`, `Rendering/`, `Commands/`, `Utils/`.
- Rebuilt on Vintage Story 1.22.0 and .NET 10 with C# 12, nullable annotations, and `internal` visibility by default.
- Snapshot JSON now includes a `Version` field. Existing 1.x snapshot files remain loadable - the `ObjectCountsByType` alias maps to the renamed `TypeCounts` property.
- Config migration is automatic: the VS config loader deserializes what it can from the old flat format, section objects get their defaults, and the first `StoreModConfig` call rewrites the file in the new sectioned layout.
- `SafeFileName` utility uses a source-generated regex for underscore collapsing.
- CI workflow updated for .NET 10 and the new solution layout.

### Fixed

- Harmony patches are now applied through a single managed class with explicit error handling. A failed patch logs a warning and allows the mod to continue functioning via `AutoTrackedBE` and periodic entity polling, instead of leaving the mod in a partially patched state.
- Snapshot retention enforcement no longer races with concurrent snapshot saves - access is gated behind a `SemaphoreSlim`.
- Thread monitoring no longer holds references to disposed API objects after server shutdown.
- Entity polling tick listener is properly unregistered on dispose.

### Removed

- The monolithic `MemLeakInspectorModSystem` class that contained all command handling, snapshot logic, thread watching, heat polling, and alert detection in a single file.
- Static `InstanceTracker` - replaced with an instance-based design.
- `MemLeakInspectorServerPatcher` (unused commented-out file from 1.x).

## [1.1.0] - 2025-05-29

### Added

- Thread tracking: `/mem threads`, `/mem threadwatch`.
- Visual in-world highlight: `/mem showheat`.
- Optional background async diffing with preview truncation.
- Snapshot subfolder config for better organization.
- Dashboard UI (HTML) for `.json`/`.csv` drag-and-drop analysis.

### Changed

- Major overhaul of snapshot and diff logic for stability.
- Improved snapshot object size estimates and memory accounting.
- Export files now use safe filenames and consistent formatting.
- Improved diff output to export to `.txt` automatically.
- Tracked instance diffs include added/removed IDs if configured.

### Fixed

- Prevented CTD from excessive diff data output in chat.
- Thread-safe cleanup of Harmony patches and trackers.
- Guarded snapshot deserialization from corrupt/missing fields.

## [1.0.0] - 2025-05-21

Initial release.

### Added

- Track live memory usage per type.
- Snapshot/compare heap usage.
- Graph and CSV export support.
- Auto-snapshot and heatmap leak detection.
- `/mem help` paging and detailed command descriptions.
- Harmony double-patch prevention.
- Listener and memory cleanup on shutdown.

[Unreleased]: https://github.com/Elocrypt/MemLeakInspector/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/Elocrypt/MemLeakInspector/compare/v1.1.0...v2.0.0
[1.1.0]: https://github.com/Elocrypt/MemLeakInspector/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/Elocrypt/MemLeakInspector/releases/tag/v1.0.0
