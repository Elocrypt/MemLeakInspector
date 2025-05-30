# Mem Leak Inspector

**Mem Leak Inspector** is a mod to help you identify memory leaks, excessive object creation, and thread anomalies. It provides a suite of commands, snapshot tracking, runtime diagnostics, and an optional web-based dashboard for snapshot analysis.

---

## Features

- **Memory Snapshots** – Capture and compare snapshots of live object types to detect leaks or garbage buildup.
- **Heatmap Diffs** – Analyze rapid memory growth or cleanup between snapshots.
- **Tracked Instance Highlighting** – Visualize exact block/entity positions of leaking objects.
- **Graphs & ASCII Charts** – Watch object trends or thread usage over time.
- **Thread Monitoring** – View active threads, states, wait reasons, and CPU usage.
- **Background Watchers** – Auto-graph snapshots or thread stats on intervals.
- **Structured CSV + JSON Export** – Export all tracked data for analysis.
- **Garbage Collection Profiling** – Track object count trends across GC cycles.
- **Configurable Ignore List** – Exclude volatile/irrelevant types like particles.
- **Browser Dashboard (Optional)** – Load `.json` or `.csv` files for visualization.

---

## Installation

1. Place the compiled `.zip` in your server’s `Mods/` directory.
2. Start your server. Use `/help mem` in chat to get started.
3. Optional: Open `index.html` in your browser to use the built-in dashboard UI.

---

## Commands

### Snapshots & Memory

| Command | Description |
|--------|-------------|
| `/mem snap [name]` | Take memory snapshot |
| `/mem list` | List saved snapshot files |
| `/mem diff <A> <B>` | Compare two snapshots |
| `/mem report <name>` | Report top memory consumers |
| `/mem summary [count]` | Summarize most common types |
| `/mem export <name>` | Export snapshot to CSV |
| `/mem heatmap <A> <B>` | Growth/shrink heatmap view |
| `/mem heatmapexport <A> <B>` | Heatmap diff CSV export |
| `/mem exportallgraphs` | Export graph data per watched type |

### Watching & Highlighting

| Command | Description |
|--------|-------------|
| `/mem watch <type> [interval]` | Track type growth |
| `/mem unwatch <type>` | Stop tracking a type |
| `/mem unwatchall` | Stop all watches |
| `/mem showheat` | Highlight leaking objects in-world |
| `/mem autosnap [interval]` | Auto memory snapshots |
| `/mem autosnapstop` | Stop auto snapshots |
| `/mem graph <type>` | ASCII bar graph of watched data |

### Thread Tools

| Command | Description |
|--------|-------------|
| `/mem threads` | Take single thread snapshot |
| `/mem threadwatch [interval]` | Start thread logging loop |
| `/mem threadwatchstop` | Stop and export thread graph |

---

## Configuration

All options are stored in `MemLeakInspectorConfig.json`.

| Key | Description |
|-----|-------------|
| `EnableAsyncCommands` | Offload `/mem` commands to background thread |
| `AutoStartThreadWatcher` | Start thread watcher on server load |
| `SnapshotSubfolders` | Separate folders for snapshots, heatmaps, threads |
| `VerboseInstanceDiff` | Include full tracked ID lists in diffs |
| `DiffPreviewLines` | Max preview lines in chat |
| `IgnoreSpikeTypeFragments` | Exclude short-lived types (e.g. "particle", "smoke") |

---

## Dashboard UI (Optional)

The included static dashboard allows `.json` or `.csv` files to be loaded in your browser.

- Fully offline and portable.
- Useful for sorting, inspecting, or filtering snapshot or thread data.

---

## Use Cases

This mod is ideal for:

- **Mod Developers** profiling garbage collection and entity memory growth.
- **Server Admins** tracking performance bottlenecks, leaks, or unusual thread spikes.
- **Debugging** invisible memory buildup caused by orphaned game systems or logic.

---

## Support & Contact

**Discord:** [Elo#111920932842450944](https://discord.com/users/111920932842450944)  
**Ko-fi:** [https://ko-fi.com/elo](https://ko-fi.com/elo)  
**ModDB:** [mods.vintagestory.at/user/elo](https://mods.vintagestory.at/user/elo)

---

## License

MIT License.
