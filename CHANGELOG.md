# Changelog

## v1.1.0 (2025-05-29)

### Added
- Thread tracking: `/mem threads`, `/mem threadwatch`
- Visual in-world highlight: `/mem showheat`
- Optional background async diffing with preview truncation
- Snapshot subfolder config for better organization
- Dashboard UI (HTML) for `.json`/`.csv` drag-and-drop analysis

### Changed
- Major overhaul of snapshot and diff logic for stability
- Improved snapshot object size estimates and memory accounting
- Export files now use safe filenames and consistent formatting
- Improved diff output to export to `.txt` automatically
- Tracked instance diffs include added/removed IDs if configured

### Fixed
- Prevented CTD from excessive diff data output in chat
- Thread-safe cleanup of Harmony patches and trackers
- Guarded snapshot deserialization from corrupt/missing fields

## v1.0.0 (2025-05-21)
Initial Release

### Features
- Track live memory usage per type
- Snapshot/compare heap usage
- Graph and CSV export support
- Auto-snapshot and heatmap leak detection
- `/mem help` paging and detailed command descriptions

### Fixes & Stability
- Prevents Harmony double-patching
- Cleans up listeners and memory on shutdown
- CTD-safe (with single-patch guard)
