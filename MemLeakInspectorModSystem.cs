using HarmonyLib;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using static MemLeakInspector.MemLeakInspectorConfig;


namespace MemLeakInspector
{
    public class MemLeakInspectorModSystem : ModSystem
    {
        private static readonly JsonSerializerOptions jsonOpts = new() { WriteIndented = true };
        private static readonly Dictionary<string, int> typeSizeCache = new();
        private static readonly Dictionary<Type, int> primitiveSizes = new()
        {
            [typeof(bool)] = 1,
            [typeof(byte)] = 1,
            [typeof(sbyte)] = 1,
            [typeof(short)] = 2,
            [typeof(ushort)] = 2,
            [typeof(int)] = 4,
            [typeof(uint)] = 4,
            [typeof(long)] = 8,
            [typeof(ulong)] = 8,
            [typeof(char)] = 2,
            [typeof(float)] = 4,
            [typeof(double)] = 8,
            [typeof(decimal)] = 16,
            [typeof(string)] = 24,  // Rough base string overhead
            [typeof(object)] = 8    // Fallback object ref
        };

        private MemSnapshot? lastAlertSnapshot = null;
        private long? alertWatcherListenerId = null;

        private Dictionary<string, WatchedType> activeWatches = new();
        private long? heatWatcherListenerId = null;

        private Dictionary<string, int> lastHeatSnapshot = new();
        private int heatThreshold = 100;
        private long? autoSnapshotListenerId = null;

        private ICoreServerAPI sapi = null!;
        private string snapshotDir = null!;

        private bool snapshotRunning = false;

        private MemLeakInspectorConfig? config;

        #region Entry Points

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            snapshotDir = Path.Combine(api.GetOrCreateDataPath("MemLeakInspector"), "snapshots");

            if (!Directory.Exists(snapshotDir))
            {
                Directory.CreateDirectory(snapshotDir);
            }

            api.RegisterBlockEntityClass("memtrackedbe", typeof(AutoTrackedBE));

            api.Event.RegisterGameTickListener(TrackLoadedEntities, 10000);

            Harmony.DEBUG = true;

            RegisterServerCommands(api);

            config = api.LoadModConfig<MemLeakInspectorConfig>("MemLeakInspectorConfig.json") ?? new MemLeakInspectorConfig();
            api.StoreModConfig(config, "MemLeakInspectorConfig.json");
            sapi.Logger.Notification("[MemLeakInspector] Initialized.");
        }

        public override void Dispose()
        {
            InstanceTracker.Clear();
            activeWatches.Clear();

            if (autoSnapshotListenerId.HasValue)
            {
                sapi.Event.UnregisterGameTickListener(autoSnapshotListenerId.Value);
                autoSnapshotListenerId = null;
            }
            if (heatWatcherListenerId.HasValue)
            {
                sapi.Event.UnregisterGameTickListener(heatWatcherListenerId.Value);
                heatWatcherListenerId = null;
            }

            sapi?.Logger.Notification("[MemLeakInspector] Unloaded.");
        }

        #endregion

        #region Command Registration

        private void RegisterServerCommands(ICoreServerAPI sapi)
        {
            sapi.ChatCommands
                .Create("mem")
                .WithDescription("Memory debugging tools (MemLeakInspector)")
                .RequiresPrivilege("controlserver")

                .BeginSubCommand("alertwatch")
                    .WithDescription("Start real-time memory/instance spike detection.")
                    .HandleWith(_ =>
                    {
                        return StartAlertWatcher();
                    })
                .EndSubCommand()

                .BeginSubCommand("alertstop")
                    .WithDescription("Stop real-time spike detection.")
                    .HandleWith(_ =>
                    {
                        return StopAlertWatcher();
                    })
                .EndSubCommand()

                .BeginSubCommand("snap")
                    .WithDescription("Take a memory snapshot. Optionally provide a name.")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("name"))
                    .HandleWith(ctx =>
                    {
                        string name = ctx.Parsers[0].GetValue() as string ?? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                        return CmdMemSnap(name);
                    })
                .EndSubCommand()

                .BeginSubCommand("list")
                    .WithDescription("List available memory snapshot files.")
                    .HandleWith(_ => CmdListSnapshots())
                .EndSubCommand()

                .BeginSubCommand("diff")
                    .WithDescription("Compare two snapshots by instance count change.")
                    .WithArgs(
                        sapi.ChatCommands.Parsers.Word("snapshotA"),
                        sapi.ChatCommands.Parsers.Word("snapshotB")
                    )
                    .HandleWith(ctx =>
                    {
                        string? snapA = ctx.Parsers[0].GetValue()?.ToString();
                        string? snapB = ctx.Parsers[1].GetValue()?.ToString();

                        if (snapA == null || snapB == null)
                            return TextCommandResult.Error("[MemLeakInspector] One or both snapshot names are null.");

                        return CmdDiffSnapshots(snapA, snapB);
                    })
                .EndSubCommand()

                .BeginSubCommand("report")
                    .WithDescription("Show top memory types in a snapshot.")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("snapshotName"))
                    .HandleWith(ctx =>
                    {
                        string? name = ctx.Parsers[0].GetValue()?.ToString();
                        if (name == null)
                            return TextCommandResult.Error("[MemLeakInspector] No snapshot name provided.");

                        return CmdReportSnapshot(name);
                    })
                .EndSubCommand()

                .BeginSubCommand("watch")
                    .WithDescription("Track a type's instance count over time.")
                    .WithArgs(
                        sapi.ChatCommands.Parsers.Word("typeName"),
                        sapi.ChatCommands.Parsers.OptionalInt("intervalSec")
                    )
                    .HandleWith(ctx =>
                    {
                        string? typeName = ctx.Parsers[0].GetValue()?.ToString();
                        int intervalSec = ctx.Parsers[1].GetValue() is int i ? Math.Clamp(i, 5, 600) : 30;

                        if (string.IsNullOrWhiteSpace(typeName))
                            return TextCommandResult.Error("[MemLeakInspector] Missing or invalid type name.");

                        return StartWatcher(typeName, intervalSec);
                    })
                .EndSubCommand()

                .BeginSubCommand("unwatch")
                    .WithDescription("Stop watching a specific type.")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("typeName"))
                    .HandleWith(ctx =>
                    {
                        string? typeName = ctx.Parsers[0].GetValue()?.ToString();
                        if (string.IsNullOrWhiteSpace(typeName))
                        {
                            return TextCommandResult.Error("[MemLeakInspector] No type specified.");
                        }

                        if (activeWatches.Remove(typeName))
                        {
                            return TextCommandResult.Success($"[MemLeakInspector] Stopped watching type: {typeName}");
                        }
                        else
                        {
                            return TextCommandResult.Error($"[MemLeakInspector] Type '{typeName}' was not being watched.");
                        }
                    })
                .EndSubCommand()

                .BeginSubCommand("unwatchall")
                    .WithDescription("Stop watching all types.")
                    .HandleWith(_ =>
                    {
                        activeWatches.Clear();
                        return TextCommandResult.Success("[MemLeakInspector] Stopped watching all types.");
                    })
                .EndSubCommand()

                .BeginSubCommand("export")
                    .WithDescription("Export a snapshot to CSV.")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("snapshotName"))
                    .HandleWith(ctx =>
                    {
                        string? name = ctx.Parsers[0].GetValue()?.ToString();
                        if (string.IsNullOrWhiteSpace(name))
                            return TextCommandResult.Error("[MemLeakInspector] Please provide a snapshot name.");

                        return ExportSnapshotToCsv(name);
                    })
                .EndSubCommand()

                .BeginSubCommand("autosnap")
                    .WithDescription("Auto-snapshot every X seconds.")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("intervalSec"))
                    .HandleWith(ctx =>
                    {
                        int interval = ctx.Parsers[0].GetValue() is int val ? Math.Clamp(val, 10, 3600) : 60;

                        StartAutoSnapshot(interval);

                        return TextCommandResult.Success($"[MemLeakInspector] Auto-snapshot started every {interval} seconds.");
                    })
                .EndSubCommand()

                .BeginSubCommand("autosnapstop")
                    .WithDescription("Stop auto-snapshotting.")
                    .HandleWith(_ =>
                    {
                        if (autoSnapshotListenerId != null)
                        {
                            sapi.Event.UnregisterGameTickListener(autoSnapshotListenerId.Value);
                            autoSnapshotListenerId = null;
                            return TextCommandResult.Success("[MemLeakInspector] Auto-snapshot stopped.");
                        }
                        return TextCommandResult.Success("[MemLeakInspector] No active auto-snapshot task.");
                    })
                .EndSubCommand()

                .BeginSubCommand("heatmap")
                    .WithDescription("Compare two snapshots for growth/shrinkage.")
                    .WithArgs(
                        sapi.ChatCommands.Parsers.Word("snapshotOld"),
                        sapi.ChatCommands.Parsers.Word("snapshotNew")
                    )
                    .HandleWith(ctx =>
                    {
                        string? snapA = ctx.Parsers[0].GetValue()?.ToString();
                        string? snapB = ctx.Parsers[1].GetValue()?.ToString();

                        if (string.IsNullOrEmpty(snapA) || string.IsNullOrEmpty(snapB))
                            return TextCommandResult.Error("[MemLeakInspector] Provide two snapshot names.");

                        return CmdHeatmap(snapA, snapB);
                    })
                .EndSubCommand()

                .BeginSubCommand("heatmapexport")
                    .WithDescription("Export snapshot delta to CSV.")
                    .WithArgs(
                        sapi.ChatCommands.Parsers.Word("oldSnapshot"),
                        sapi.ChatCommands.Parsers.Word("newSnapshot")
                    )
                    .HandleWith(ctx =>
                    {
                        string? nameA = ctx.Parsers[0].GetValue()?.ToString();
                        string? nameB = ctx.Parsers[1].GetValue()?.ToString();

                        if (string.IsNullOrEmpty(nameA) || string.IsNullOrEmpty(nameB))
                            return TextCommandResult.Error("[MemLeakInspector] Provide two snapshot names.");

                        return ExportHeatmapCsv(nameA, nameB);
                    })
                .EndSubCommand()

                .BeginSubCommand("watchheat")
                    .WithDescription("Monitor for fast-growing types (≥ threshold).")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("threshold"))
                    .HandleWith(ctx =>
                    {
                        heatThreshold = ctx.Parsers[0].GetValue() is int val ? Math.Max(1, val) : 100;
                        StartHeatWatcher();

                        return TextCommandResult.Success($"[MemLeakInspector] Watching for leaks ≥ {heatThreshold} objects per type.");
                    })
                .EndSubCommand()

                .BeginSubCommand("watchheatstop")
                    .WithDescription("Stop the live memory leak watcher.")
                    .HandleWith(_ =>
                    {
                        if (heatWatcherListenerId != null)
                        {
                            sapi.Event.UnregisterGameTickListener(heatWatcherListenerId.Value);
                            heatWatcherListenerId = null;
                            return TextCommandResult.Success("[MemLeakInspector] Watchheat stopped.");
                        }

                        return TextCommandResult.Success("[MemLeakInspector] No active watchheat listener.");
                    })
                .EndSubCommand()
                .BeginSubCommand("summary")
                    .WithDescription("Show top types from last N snapshots.")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("count"))
                    .HandleWith(ctx =>
                    {
                        int limit = ctx.Parsers[0].GetValue() is int val ? Math.Clamp(val, 1, 100) : 10;
                        return CmdSummary(limit);
                    })
                .EndSubCommand()

                .BeginSubCommand("graph")
                    .WithDescription("Export time-series CSV for a type.")
                    .WithArgs(
                        sapi.ChatCommands.Parsers.Word("typeName"),
                        sapi.ChatCommands.Parsers.OptionalInt("count")
                    )
                    .HandleWith(ctx =>
                    {
                        string? type = ctx.Parsers[0].GetValue()?.ToString();
                        int limit = ctx.Parsers[1].GetValue() is int val ? Math.Clamp(val, 1, 1000) : 20;

                        if (string.IsNullOrWhiteSpace(type))
                            return TextCommandResult.Error("[MemLeakInspector] Provide a type name.");

                        return CmdGraphType(type, limit);
                    })
                .EndSubCommand()

                .BeginSubCommand("exportallgraphs")
                    .WithDescription("Export all watched graphs to CSV.")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("limit"))
                    .HandleWith(ctx =>
                    {
                        int limit = ctx.Parsers[0].GetValue() is int i ? i : 30;
                        return ExportAllGraphs(limit);
                    })
                .EndSubCommand()

                .BeginSubCommand("memusage")
                    .WithDescription("Show estimated memory usage by type from a snapshot: /mem memusage <snapshotName>")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("snapshotName"))
                    .HandleWith(ctx =>
                    {
                        string? name = ctx.Parsers[0].GetValue()?.ToString();
                        if (string.IsNullOrWhiteSpace(name))
                            return TextCommandResult.Error("[MemLeakInspector] Please provide a snapshot name.");

                        return ShowMemoryUsageFromSnapshot(name);
                    })
                .EndSubCommand();

            sapi.Logger.Notification("[MemLeakInspector] Chat commands registered.");

            bool patched = false;

            sapi.Event.PlayerJoin += (plr) =>
            {
                if (patched) return;
                patched = true;

                try
                {
                    var harmony = new Harmony("memleakinspector.autotrack");
                    harmony.PatchAll();
                    sapi.Logger.Notification("[MemLeakInspector] Harmony patches applied on player join.");
                }
                catch (Exception ex)
                {
                    sapi.Logger.Error($"[MemLeakInspector] Failed to patch Harmony: {ex.Message}");
                }
            };
        }

        #endregion

        #region Command Logic

        private TextCommandResult StartAlertWatcher()
        {
            if (alertWatcherListenerId != null)
                return TextCommandResult.Error("[MemLeakInspector] Alert watcher is already running.");

            lastAlertSnapshot = TakeSnapshot();

            alertWatcherListenerId = sapi.Event.RegisterGameTickListener(dt =>
            {
                if (!snapshotRunning)
                {
                    snapshotRunning = true;

                    Task.Run(() =>
                    {
                        try
                        {
                            var current = TakeSnapshot();
                            SaveSnapshotToDisk(current);

                            if (lastAlertSnapshot == null) {
                                lastAlertSnapshot = current;
                                return;
                            }

                            var oldCounts = lastAlertSnapshot.ObjectCountsByType;
                            var newCounts = current.ObjectCountsByType;

                            var spikeThreshold = config?.AlertInstanceSpike ?? 500;
                            var memoryThreshold = (config?.AlertMemorySpikeMB ?? 100.0) * 1024 * 1024;

                            long memDelta = current.TotalManagedMemoryBytes - lastAlertSnapshot.TotalManagedMemoryBytes;
                            if (memDelta >= memoryThreshold)
                            {
                                sapi.Logger.Warning($"[MemLeakInspector] MEMORY SPIKE: +{memDelta / (1024 * 1024)} MB");
                            }

                            foreach (var key in newCounts.Keys)
                            {
                                if (IsIgnoredType(key)) continue;
                                int old = oldCounts.TryGetValue(key, out var o) ? o : 0;
                                int now = newCounts[key];
                                int delta = now - old;

                                if (delta >= spikeThreshold)
                                {
                                    sapi.Logger.Warning($"[MemLeakInspector] INSTANCE SPIKE: {key} grew by {delta} ({old} → {now})");
                                }
                            }

                            lastAlertSnapshot = current;
                        }
                        catch (Exception ex)
                        {
                            sapi.Logger.Error("[MemLeakInspector] Snapshot failed: " + ex.Message);
                        }
                        finally
                        {
                            snapshotRunning = false;
                        }
                    });
                }
            }, (config?.AlertCheckIntervalSec ?? 30) * 1000);
            return TextCommandResult.Success("[MemLeakInspector] Alert watcher started.");
        }

        private TextCommandResult StopAlertWatcher()
        {
            if (alertWatcherListenerId != null)
            {
                sapi.Event.UnregisterGameTickListener(alertWatcherListenerId.Value);
                alertWatcherListenerId = null;
                return TextCommandResult.Success("[MemLeakInspector] Alert watcher stopped.");
            }
            return TextCommandResult.Success("[MemLeakInspector] No alert watcher running.");
        }

        private TextCommandResult CmdMemSnap(string name)
        {
            Task.Run(() =>
            {
                try
                {
                    var snapshot = TakeSnapshot();
                    var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                    var filePath = Path.Combine(snapshotDir, $"{name}.json");
                    File.WriteAllText(filePath, json);

                    sapi.Logger.Notification($"[MemLeakInspector] Snapshot '{name}' saved ({snapshot.TotalManagedMemoryBytes / 1024 / 1024} MB)");
                }
                catch (Exception ex)
                {
                    sapi.Logger.Error($"[MemLeakInspector] Snapshot failed: {ex.Message}");
                }
            });

            return TextCommandResult.Success("[MemLeakInspector] Snapshotting in background...");
        }

        private TextCommandResult CmdDiffSnapshots(string nameA, string nameB)
        {
            string fileA = Path.Combine(snapshotDir, $"{nameA}.json");
            string fileB = Path.Combine(snapshotDir, $"{nameB}.json");

            if (!File.Exists(fileA)) return TextCommandResult.Error($"Snapshot '{nameA}' not found.");
            if (!File.Exists(fileB)) return TextCommandResult.Error($"Snapshot '{nameB}' not found.");

            try
            {
                var snapA = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(fileA));
                var snapB = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(fileB));

                if (snapA == null || snapB == null)
                    return TextCommandResult.Error("Failed to load one or both snapshots.");

                var resultLines = new List<string> {
                    $"[MemLeakInspector] Snapshot diff: {nameA} → {nameB}",
                    $"Memory: {snapA.TotalManagedMemoryBytes / 1024 / 1024} MB → {snapB.TotalManagedMemoryBytes / 1024 / 1024} MB",
                    "Changed types:"
                };

                var allKeys = new HashSet<string>(snapA.ObjectCountsByType.Keys);
                allKeys.UnionWith(snapB.ObjectCountsByType.Keys);

                foreach (var key in allKeys.OrderBy(k => k))
                {
                    snapA.ObjectCountsByType.TryGetValue(key, out int countA);
                    snapB.ObjectCountsByType.TryGetValue(key, out int countB);
                    int delta = countB - countA;

                    if (delta != 0)
                        resultLines.Add($"{(delta > 0 ? "+" : "")}{delta,4}  {key}");
                }

                if (resultLines.Count == 3)
                    resultLines.Add("No differences detected.");

                return TextCommandResult.Success(string.Join("\n", resultLines));
            }
            catch (Exception ex)
            {
                return TextCommandResult.Error($"[MemLeakInspector] Error diffing snapshots: {ex.Message}");
            }
        }

        private TextCommandResult CmdReportSnapshot(string name)
        {
            string filePath = Path.Combine(snapshotDir, $"{name}.json");

            if (!File.Exists(filePath))
                return TextCommandResult.Error($"[MemLeakInspector] Snapshot '{name}' not found.");

            try
            {
                MemSnapshot? snap = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(filePath));
                if (snap == null)
                    return TextCommandResult.Error($"[MemLeakInspector] Failed to load snapshot '{name}'.");

                var topTypes = snap.ObjectCountsByType
                    .OrderByDescending(kv => kv.Value)
                    .Take(10)
                    .ToList();

                var result = new List<string>
                {
                    $"[MemLeakInspector] Snapshot Report: '{name}'",
                    $"Total Heap: {snap.TotalManagedMemoryBytes / 1024 / 1024} MB",
                    $"Top {topTypes.Count} types by instance count:"
                };

                foreach (var (type, count) in topTypes)
                    result.Add($"{count,6} × {type}");

                return TextCommandResult.Success(string.Join("\n", result));
            }
            catch (Exception ex)
            {
                return TextCommandResult.Error($"[MemLeakInspector] Report failed: {ex.Message}");
            }
        }

        private TextCommandResult StartWatcher(string typeName, int intervalSec)
        {
            if (activeWatches.ContainsKey(typeName))
                return TextCommandResult.Error($"[MemLeakInspector] Already watching '{typeName}'.");

            var watch = new WatchedType
            {
                TypeName = typeName,
                IntervalSec = intervalSec,
                LastCount = 0
            };

            activeWatches[typeName] = watch;

            sapi.Event.RegisterGameTickListener(dt => PollWatchedType(typeName), intervalSec * 1000);

            return TextCommandResult.Success($"[MemLeakInspector] Now watching '{typeName}' every {intervalSec}s.");
        }

        private TextCommandResult ExportSnapshotToCsv(string name)
        {
            string jsonPath = Path.Combine(snapshotDir, $"{name}.json");
            string csvPath = Path.Combine(snapshotDir, $"{name}.csv");

            if (!File.Exists(jsonPath))
                return TextCommandResult.Error($"[MemLeakInspector] Snapshot '{name}' not found.");

            try
            {
                var snapshot = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(jsonPath));
                if (snapshot == null)
                    return TextCommandResult.Error("[MemLeakInspector] Failed to load snapshot.");

                using var writer = new StreamWriter(csvPath);
                writer.WriteLine("TypeName,InstanceCount");

                foreach (var kvp in snapshot.ObjectCountsByType.OrderByDescending(kv => kv.Value))
                {
                    writer.WriteLine($"\"{kvp.Key}\",{kvp.Value}");
                }

                return TextCommandResult.Success($"[MemLeakInspector] Snapshot '{name}' exported to CSV.");
            }
            catch (Exception ex)
            {
                return TextCommandResult.Error($"[MemLeakInspector] Export failed: {ex.Message}");
            }
        }

        private TextCommandResult CmdHeatmap(string oldName, string newName)
        {
            string pathA = Path.Combine(snapshotDir, $"{oldName}.json");
            string pathB = Path.Combine(snapshotDir, $"{newName}.json");

            if (!File.Exists(pathA) || !File.Exists(pathB))
                return TextCommandResult.Error("[MemLeakInspector] One or both snapshot files not found.");

            var oldSnap = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(pathA));
            var newSnap = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(pathB));

            if (oldSnap == null || newSnap == null)
                return TextCommandResult.Error("[MemLeakInspector] Failed to load snapshot(s).");

            var deltas = new Dictionary<string, int>();

            foreach (var key in oldSnap.ObjectCountsByType.Keys.Union(newSnap.ObjectCountsByType.Keys))
            {
                int oldVal = oldSnap.ObjectCountsByType.TryGetValue(key, out var v1) ? v1 : 0;
                int newVal = newSnap.ObjectCountsByType.TryGetValue(key, out var v2) ? v2 : 0;

                int delta = newVal - oldVal;
                if (delta != 0)
                    deltas[key] = delta;
            }

            if (deltas.Count == 0)
                return TextCommandResult.Success("[MemLeakInspector] No changes detected.");

            sapi.Logger.Notification($"[MemLeakInspector] Heatmap: {oldName} → {newName}");

            foreach (var entry in deltas.OrderByDescending(kv => Math.Abs(kv.Value)).Take(10))
            {
                string sign = entry.Value > 0 ? "+" : "";
                sapi.Logger.Notification($"  {sign}{entry.Value,5}  {entry.Key}");
            }

            return TextCommandResult.Success("[MemLeakInspector] Heatmap generated.");
        }

        private TextCommandResult ExportHeatmapCsv(string oldName, string newName)
        {
            string pathA = Path.Combine(snapshotDir, $"{oldName}.json");
            string pathB = Path.Combine(snapshotDir, $"{newName}.json");
            string exportPath = Path.Combine(snapshotDir, $"heatmap_{oldName}_to_{newName}.csv");

            if (!File.Exists(pathA) || !File.Exists(pathB))
                return TextCommandResult.Error("[MemLeakInspector] One or both snapshot files not found.");

            var oldSnap = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(pathA));
            var newSnap = JsonSerializer.Deserialize<MemSnapshot>(File.ReadAllText(pathB));

            if (oldSnap == null || newSnap == null)
                return TextCommandResult.Error("[MemLeakInspector] Failed to load snapshot(s).");

            var deltas = new List<(string type, int delta)>();

            foreach (var key in oldSnap.ObjectCountsByType.Keys.Union(newSnap.ObjectCountsByType.Keys))
            {
                int oldVal = oldSnap.ObjectCountsByType.TryGetValue(key, out var v1) ? v1 : 0;
                int newVal = newSnap.ObjectCountsByType.TryGetValue(key, out var v2) ? v2 : 0;

                int delta = newVal - oldVal;
                if (delta != 0)
                    deltas.Add((key, delta));
            }

            using var writer = new StreamWriter(exportPath);
            writer.WriteLine("TypeName,Delta");

            foreach (var entry in deltas.OrderByDescending(e => Math.Abs(e.delta)))
            {
                writer.WriteLine($"\"{entry.type}\",{entry.delta}");
            }

            return TextCommandResult.Success($"[MemLeakInspector] Heatmap exported to: {Path.GetFileName(exportPath)}");
        }

        private TextCommandResult CmdSummary(int snapshotCount)
        {
            var files = Directory.GetFiles(snapshotDir, "*.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(snapshotCount)
                .ToList();

            if (files.Count == 0)
                return TextCommandResult.Error("[MemLeakInspector] No snapshots available.");

            var totals = new Dictionary<string, int>();

            foreach (var file in files)
            {
                var json = File.ReadAllText(file);
                var snap = JsonSerializer.Deserialize<MemSnapshot>(json);

                if (snap?.ObjectCountsByType == null)
                    continue;

                foreach (var kvp in snap.ObjectCountsByType)
                {
                    if (!totals.ContainsKey(kvp.Key))
                        totals[kvp.Key] = 0;

                    totals[kvp.Key] += kvp.Value;
                }
            }

            if (totals.Count == 0)
                return TextCommandResult.Success("[MemLeakInspector] Summary found no tracked types.");

            sapi.Logger.Notification($"[MemLeakInspector] Summary across {files.Count} snapshots:");

            foreach (var entry in totals.OrderByDescending(kv => kv.Value).Take(10))
            {
                sapi.Logger.Notification($"  {entry.Value,5} × {entry.Key}");
            }

            return TextCommandResult.Success("[MemLeakInspector] Summary complete.");
        }

        private TextCommandResult CmdGraphType(string typeName, int limit)
        {
            var files = Directory.GetFiles(snapshotDir, "*.json")
                .OrderByDescending(File.GetLastWriteTime)
                .Take(limit)
                .OrderBy(File.GetLastWriteTime) // Restore chronological order
                .ToList();

            if (files.Count == 0)
                return TextCommandResult.Error("[MemLeakInspector] No snapshot files found.");

            string exportPath = Path.Combine(snapshotDir, $"graph_{typeName.Replace(":", "_")}.csv");

            using var writer = new StreamWriter(exportPath);
            writer.WriteLine("Timestamp,InstanceCount");

            foreach (var file in files)
            {
                var json = File.ReadAllText(file);
                var snap = JsonSerializer.Deserialize<MemSnapshot>(json);

                sapi.Logger.Notification($"[MemLeakInspector] Reading {Path.GetFileName(file)} → Timestamp: {snap?.Timestamp:O}");


                if (snap == null || snap.Timestamp == default || snap.ObjectCountsByType == null)
                    continue;

                var matching = snap.ObjectCountsByType
                    .Where(kvp => kvp.Key.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int count = matching.Sum(kvp => kvp.Value);

                writer.WriteLine($"{snap.Timestamp:yyyy-MM-dd HH:mm:ss},{count}");

                sapi.Logger.Notification($"[MemLeakInspector] Snapshot {Path.GetFileName(file)} matched count: {count}");
            }

            return TextCommandResult.Success($"[MemLeakInspector] Graph exported to: {Path.GetFileName(exportPath)}");
        }

        private TextCommandResult ExportAllGraphs(int limit)
        {
            if (activeWatches.Count == 0)
                return TextCommandResult.Error("[MemLeakInspector] No active watches to export.");

            foreach (string typeName in activeWatches.Keys)
            {
                var result = CmdGraphType(typeName, limit);
                sapi.Logger.Notification($"[MemLeakInspector] Exported graph for {typeName}: {result}");
            }

            return TextCommandResult.Success("[MemLeakInspector] All watched graphs exported.");
        }

        private TextCommandResult ShowMemoryUsageFromSnapshot(string snapshotName)
        {
            var path = Path.Combine(snapshotDir, $"{snapshotName}.json");
            if (!File.Exists(path))
                return TextCommandResult.Error($"[MemLeakInspector] Snapshot '{snapshotName}' not found.");

            var json = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<MemSnapshot>(json);
            if (snapshot?.EstimatedBytesPerType != null)
            {
                foreach (var kvp in snapshot.EstimatedBytesPerType)
                    typeSizeCache[kvp.Key] = kvp.Value;
            }

            if (snapshot?.EstimatedMemoryBytesPerType == null || snapshot.EstimatedMemoryBytesPerType.Count == 0)
                return TextCommandResult.Error("[MemLeakInspector] Snapshot is missing memory data.");

            sapi.Logger.Notification($"[MemLeakInspector] Estimated memory usage in '{snapshotName}':");

            foreach (var entry in snapshot.EstimatedMemoryBytesPerType
                .OrderByDescending(kv => kv.Value))
            {
                double estimatedMB = entry.Value / (1024.0 * 1024.0);

                if (estimatedMB < config?.ReportFilterMB)
                    continue;
                sapi.Logger.Notification($"    {entry.Key} = {estimatedMB:F1} MB");
            }

            return TextCommandResult.Success("[MemLeakInspector] Memory usage report complete.");
        }

        #endregion

        #region Snapshot Logic

        private static MemSnapshot TakeSnapshot()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var counts = InstanceTracker.GetLiveCounts();

            var snapshot = new MemSnapshot
            {
                Timestamp = DateTime.UtcNow,
                TotalManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: true),
                ObjectCountsByType = counts,
                EstimatedBytesPerType = new Dictionary<string, int>(typeSizeCache),
                EstimatedMemoryBytesPerType = counts.ToDictionary(
                    kvp => kvp.Key,
                    kvp => EstimateTypeSize(kvp.Key, kvp.Value)
                )
            };

            Console.WriteLine($"[DEBUG] Snapshot contains {snapshot.ObjectCountsByType.Count} tracked types.");
            return snapshot;
        }

        private void SaveFilteredSnapshot(string typeFilter)
        {
            var snap = new MemSnapshot
            {
                Timestamp = DateTime.UtcNow,
                ObjectCountsByType = InstanceTracker.GetLiveCounts()
                    .Where(kvp => kvp.Key.Contains(typeFilter, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };

            var path = Path.Combine(snapshotDir, $"{snap.Timestamp:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(snap));
            sapi.Logger.Notification($"[MemLeakInspector] Snapshot filtered by '{typeFilter}' written to: {path}");
        }

        private IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        #endregion

        #region Helpers

        private TextCommandResult CmdListSnapshots()
        {
            if (!Directory.Exists(snapshotDir))
                return TextCommandResult.Error("[MemLeakInspector] Snapshot folder not found.");

            string[] files = Directory.GetFiles(snapshotDir, "*.json");
            if (files.Length == 0)
                return TextCommandResult.Success("[MemLeakInspector] No snapshots found.");

            string output = "[MemLeakInspector] Snapshots:\n" + string.Join("\n", files.Select(f => Path.GetFileName(f)));
            return TextCommandResult.Success(output);
        }

        private void TrackLoadedEntities(float dt)
        {
            foreach (var entity in sapi.World.LoadedEntities.Values)
            {
                InstanceTracker.RegisterObject(entity);
            }
        }

        private void PollWatchedType(string typeName)
        {
            if (!activeWatches.TryGetValue(typeName, out var watch)) return;

            var tracked = InstanceTracker.GetLiveCounts();
            tracked.TryGetValue(typeName, out int currentCount);

            int delta = currentCount - watch.LastCount;
            watch.LastCount = currentCount;

            string status = delta switch
            {
                > 50 => "LEAKING",
                > 5 => "Growing",
                < -5 => "Shrinking",
                _ => "Stable"
            };

            sapi.Logger.Notification($"[MemLeakInspector] {status}: {typeName} = {currentCount} ({(delta >= 0 ? "+" : "")}{delta})");
        }

        private void SaveSnapshotToDisk(MemSnapshot snap)
        {
            var json = JsonSerializer.Serialize(snap, jsonOpts);
            var path = Path.Combine(snapshotDir, $"{snap.Timestamp:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, json);

            sapi.Logger.Notification($"[MemLeakInspector] Snapshot saved: {Path.GetFileName(path)}");
        }

        private void StartAutoSnapshot(int intervalSec)
        {
            if (autoSnapshotListenerId.HasValue)
            {
                sapi.Event.UnregisterGameTickListener(autoSnapshotListenerId.Value);
                autoSnapshotListenerId = null;
            }

            autoSnapshotListenerId = sapi.Event.RegisterGameTickListener((dt) =>
            {
                string name = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                CmdMemSnap(name);
            }, intervalSec * 1000);
        }

        private void StartHeatWatcher()
        {
            if (heatWatcherListenerId != null)
            {
                sapi.Event.UnregisterGameTickListener(heatWatcherListenerId.Value);
                heatWatcherListenerId = null;
            }

            var init = InstanceTracker.GetLiveCounts();
            if (init.Count == 0)
            {
                sapi.Logger.Warning("[MemLeakInspector] No tracked entities found. Heat watcher will not start.");
                return;
            }

            sapi.Logger.Notification($"[MemLeakInspector] Watchheat started. Active watchers: {activeWatches.Count}");

            lastHeatSnapshot = init;

            heatWatcherListenerId = sapi.Event.RegisterGameTickListener(dt =>
            {
                var current = InstanceTracker.GetLiveCounts();

                foreach (var key in current.Keys)
                {
                    int old = lastHeatSnapshot.TryGetValue(key, out var v) ? v : 0;
                    int now = current[key];
                    int diff = now - old;

                    if (diff >= heatThreshold)
                    {
                        sapi.Logger.Notification($"[MemLeakInspector] LEAK ALERT: {key} increased by {diff} ({old} → {now})");
                    }
                }

                lastHeatSnapshot = current;
                foreach (var typeName in activeWatches.Keys)
                {
                    SaveFilteredSnapshot(typeName);
                }

                ExportAllGraphs(100);
                // Tweak limit maybe

            }, 10000); // every 10s
        }

        private static long EstimateTypeSize(string typeName, int instanceCount)
        {
            if (typeSizeCache.TryGetValue(typeName, out int cached))
                return (long)cached * instanceCount;

            try
            {
                Type? type = Type.GetType(typeName, throwOnError: false);
                if (type == null)
                    return instanceCount * 300; // fallback if type not found

                int size = 0;

                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.FieldType.IsValueType && primitiveSizes.TryGetValue(field.FieldType, out int primSize))
                    {
                        size += primSize;
                    }
                    else if (field.FieldType == typeof(string))
                    {
                        size += primitiveSizes[typeof(string)];
                    }
                    else
                    {
                        size += 8; // Pointer/reference size assumption
                    }
                }

                if (size == 0)
                    size = 300; // fallback for types with no visible fields

                typeSizeCache[typeName] = size;
                return (long)size * instanceCount;
            }
            catch
            {
                return instanceCount * 300;
            }
        }

        private bool IsIgnoredType(string typeName)
        {
            return config.IgnoreSpikeTypeFragments.Any(fragment =>
                typeName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0
            );
        }

        private class WatchedType
        {
            public string TypeName = "";
            public int IntervalSec = 30;
            public int LastCount = 0;
        }

        #endregion
    }
}