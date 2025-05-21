# Mem Leak Inspector

Mem Leak Inspector is a developer-focused Vintage Story server mod to help track memory leaks or excessive object creation over time by monitoring instance counts.

## Features
- Take and compare memory snapshots
- Track object count changes between snapshots
- Heatmap diffs to detect fast-growing or shrinking types
- Auto-snapshot and auto-graph generation
- Graph object counts over time
- Export to CSV for analysis

## Installation
1. Place the compiled `.dll` in your server's `Mods/` folder.
2. Start the server.

## Commands

| Command | Description |
|--------|-------------|
| `/mem snap [name]` | Take memory snapshot |
| `/mem list` | List snapshot files |
| `/mem diff <A> <B>` | Compare two snapshots |
| `/mem report <name>` | Top memory consumers |
| `/mem export <name>` | Export snapshot to CSV |
| `/mem graph <type> [limit]` | Graph one type |
| `/mem summary [count]` | Aggregate top types |
| `/mem watch <type> [interval]` | Watch a type over time |
| `/mem unwatch <type>` | Stop watching a type |
| `/mem unwatchall` | Stop all watchers |
| `/mem watchheat [threshold]` | Detect fast growth types |
| `/mem watchheatstop` | Stop watchheat |
| `/mem autosnap [interval]` | Auto snapshot loop |
| `/mem autosnapstop` | Stop auto snapshot |
| `/mem heatmap <old> <new>` | Show growth/shrinkage |
| `/mem heatmapexport <old> <new>` | Export heatmap CSV |
| `/mem exportallgraphs [limit]` | Export all watched graphs |

## Help
Use `/mem help 1`, `/mem help 2`, `/mem help 3` for full command help in-game.

## Use Case
Perfect for mod developers, server admins, and tool authors investigating memory spikes or leak candidates.

## License
MIT License.
