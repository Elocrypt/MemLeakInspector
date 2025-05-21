# Changelog

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
