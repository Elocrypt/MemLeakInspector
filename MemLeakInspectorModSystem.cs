using HarmonyLib;
using System.Reflection;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;


namespace MemLeakInspector
{
    /// <summary>
    /// This class defines the mod system for MemLeakInspector, a tool used to monitor and debug memory leaks in Vintage Story servers.
    /// It provides command-based interfaces for taking snapshots, tracking object types, watching for spikes, and exporting diagnostics.
    /// </summary>
    /// <remarks>
    /// <para><b>Developer Notes:</b></para>
    /// <para>- Snapshots are serialized into JSON and saved to disk. Each snapshot contains instance counts and optionally tracked instance IDs.</para>
    /// <para>- The mod leverages weak references and runtime reflection to monitor live objects without impacting GC behavior.</para>
    /// <para>- Harmony patches are applied to automatically register entities and block entities for tracking.</para>
    /// <para>- Use /mem snap, /mem diff, /mem heatmap, etc. to interact via chat commands.</para>
    /// <para>- To reduce false positives, use IgnoreSpikeTypeFragments in the config to exclude known volatile objects like particles.</para>
    /// </remarks>
    public class MemLeakInspectorModSystem : ModSystem
    {
        /// <summary>
        /// Configurable serializer settings for JSON output.
        /// Used when saving snapshot data to disk.
        /// </summary>
        private static readonly JsonSerializerOptions jsonOpts = new() { WriteIndented = true };

        /// <summary>
        /// Cache of estimated sizes of each type, used for memory estimation reports.
        /// </summary>
        private static readonly Dictionary<string, int> typeSizeCache = new();

        /// <summary>
        /// Hardcoded sizes for primitive types used when estimating memory usage.
        /// These values are used as a fallback when reflecting on unknown fields.
        /// </summary>
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
            [typeof(string)] = 24,  // Estimated string object header size
            [typeof(object)] = 8    // Estimated reference pointer size
        };

        /// <summary>
        /// The most recent snapshot used for detecting memory spikes.
        /// Only updated when alert watcher is enabled.
        /// </summary>
        private MemSnapshot? lastAlertSnapshot = null;

        /// <summary>
        /// Game tick listener ID for the alert watcher task.
        /// Used to stop the task when disabling the watcher.
        /// </summary>
        private long? alertWatcherListenerId = null;

        /// <summary>
        /// Active type watchers that track individual types' instance growth over time.
        /// </summary>
        private Dictionary<string, WatchedType> activeWatches = new();

        /// <summary>
        /// Listener ID for real-time heatmap leak detection.
        /// </summary>
        private long? heatWatcherListenerId = null;

        /// <summary>
        /// Instance counts per type from the last heatmap polling interval.
        /// Used to compare against the current state to detect growth deltas.
        /// </summary>
        private Dictionary<string, int> lastHeatSnapshot = new();

        /// <summary>
        /// Minimum number of new instances required to consider a type as "leaking".
        /// </summary>
        private int heatThreshold = 100;

        /// <summary>
        /// Listener ID for automated snapshot collection.
        /// </summary>
        private long? autoSnapshotListenerId = null;

        /// <summary>
        /// Server API instance used throughout the mod for file I/O, logging, and command handling.
        /// </summary>
        private ICoreServerAPI sapi = null!;

        /// <summary>
        /// Absolute path where snapshot JSON and CSV files will be saved.
        /// </summary>
        private string snapshotDir = null!;

        /// <summary>
        /// Whether a snapshot is currently being processed.
        /// This helps prevent overlapping snapshot jobs.
        /// </summary>
        private bool snapshotRunning = false;


        private bool threadWatcherRunning = false;


        private int threadWatcherIntervalSec = 30;


        private string threadWatcherFilename = "";


        private List<(DateTime Time, int Count)> threadWatcherHistory = new();

        /// <summary>
        /// Loaded configuration values from MemLeakInspectorConfig.json.
        /// </summary>
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
            
            if (config.AutoStartThreadWatcher)
            {
                threadWatcherRunning = true;
                threadWatcherIntervalSec = Math.Max(2, config.ThreadWatcherIntervalSeconds);
                threadWatcherFilename = Path.Combine(snapshotDir, $"threadlog-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                threadWatcherHistory.Clear();
                sapi.World.RegisterCallback(ThreadWatcherTick, 1000);
                sapi.Logger.Notification("[MemLeakInspector] Auto-started thread watcher.");
            }

            config.IgnoreSpikeTypeFragments = config.IgnoreSpikeTypeFragments
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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
                .EndSubCommand()

                .BeginSubCommand("tp")
                    .WithDescription("Teleport to a tracked instance ID")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("id"))
                    .HandleWith(ctx =>
                    {
                        var player = ctx.Caller.Player;
                        var id = ctx.Parsers[0].GetValue() as string;

                        var pos = InstanceTracker.GetPositionById(id);
                        if (pos == null)
                            return TextCommandResult.Error($"[MemLeakInspector] No instance found with ID prefix '{id}'.");

                        player.Entity.TeleportToDouble(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
                        return TextCommandResult.Success($"[MemLeakInspector] Teleported to instance {id}.");
                    })
                .EndSubCommand()

                .BeginSubCommand("showheat")
                    .WithDescription("Highlight leaking instances in the world based on snapshot delta.")
                    .HandleWith(ctx =>
                    {
                        return CmdShowHeat();
                    })
                .EndSubCommand()

                .BeginSubCommand("threads")
                    .WithDescription("Show current server process thread stats.")
                    .HandleWith(ctx => CmdListThreads())
                .EndSubCommand()

                .BeginSubCommand("threadwatch")
                    .WithDescription("Start background thread state logging.")
                    .HandleWith(ctx => {
                        int interval = 30;

                        if (ctx.Parsers.Count > 0)
                        {
                            var rawArg = ctx.Parsers[0].GetValue() as string;

                            if (int.TryParse(rawArg, out var parsed))
                                interval = parsed;
                        }
                        return CmdStartThreadWatch(interval);
                    })
                .EndSubCommand()


                .BeginSubCommand("threadwatchstop")
                    .WithDescription("Stop background thread state logging.")
                    .HandleWith(ctx => CmdStopThreadWatch())
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

        /// <summary>
        /// Begins monitoring for sudden memory or instance count spikes.
        /// </summary>
        /// <returns>A command result indicating if the watcher was started.</returns>
        /// <remarks>
        /// Uses a recurring task that compares snapshots on an interval.
        /// Alerts are logged for memory deltas or instance type growth beyond thresholds.
        /// </remarks>
        private TextCommandResult StartAlertWatcher()
        {
            if (alertWatcherListenerId != null)
                return TextCommandResult.Error("[MemLeakInspector] Alert watcher is already running.");

            lastAlertSnapshot = TakeSnapshot(config);

            alertWatcherListenerId = sapi.Event.RegisterGameTickListener(dt =>
            {
                if (!snapshotRunning)
                {
                    snapshotRunning = true;

                    Task.Run(() =>
                    {
                        try
                        {
                            var current = TakeSnapshot(config);
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

        /// <summary>
        /// Stops the alert watcher task.
        /// </summary>
        /// <returns>A success result whether or not the watcher was running.</returns>
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

        /// <summary>
        /// Saves a snapshot of the current state to a uniquely named JSON file.
        /// </summary>
        /// <param name="name">The custom or auto-generated filename (no extension).</param>
        /// <returns>Success message indicating that snapshotting has started.</returns>
        /// <remarks>
        /// Runs in a background thread to avoid stalling server tick. JSON is saved in the mod's snapshot directory.
        /// </remarks>
        private TextCommandResult CmdMemSnap(string name)
        {
            Task.Run(() =>
            {
                try
                {
                    var snapshot = TakeSnapshot(config);
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

        /// <summary>
        /// Compares two snapshots and logs a diff of object counts and tracked IDs.
        /// </summary>
        /// <param name="nameA">The older snapshot filename (without extension).</param>
        /// <param name="nameB">The newer snapshot filename (without extension).</param>
        /// <returns>A formatted diff log as chat output and disk export.</returns>
        /// <remarks>
        /// Tracks both instance counts and specific added/removed instance IDs if available in both snapshots.
        /// </remarks>
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
                if (config!.TrackIndividualEntities &&
                    snapA.TrackedInstancesByType != null &&
                    snapB.TrackedInstancesByType != null)
                {
                    resultLines.Add("Changed instances:");

                    foreach (var type in snapA.TrackedInstancesByType.Keys)
                    {
                        if (!snapB.TrackedInstancesByType.TryGetValue(type, out var newSet))
                            continue;

                        var oldSet = snapA.TrackedInstancesByType[type];
                        var added = newSet.Except(oldSet).ToList();
                        var removed = oldSet.Except(newSet).ToList();

                        if (added.Count > 0 || removed.Count > 0)
                        {
                            string formattedName = FormatTypeName(type);
                            resultLines.Add($"• {formattedName} → +{added.Count}, -{removed.Count}");

                            if (config.VerboseInstanceDiff)
                            {
                                foreach (var id in added)
                                    resultLines.Add($"    + ID: {id}");
                                foreach (var id in removed)
                                    resultLines.Add($"    - ID: {id}");
                            }
                        }
                    }
                }

                if (resultLines.Count == 3)
                    resultLines.Add("No differences detected.");

                string exportFile = Path.Combine(snapshotDir, $"diff_{nameA}_to_{nameB}.txt");
                File.WriteAllLines(exportFile, resultLines);
                resultLines.Add($"Diff exported to: {Path.GetFileName(exportFile)}");


                return TextCommandResult.Success(string.Join("\n", resultLines));
            }
            catch (Exception ex)
            {
                return TextCommandResult.Error($"[MemLeakInspector] Error diffing snapshots: {ex.Message}");
            }
        }

        /// <summary>
        /// Reports the top 10 most common object types from a snapshot.
        /// </summary>
        /// <param name="name">The snapshot name (without extension).</param>
        /// <returns>A formatted report of top object counts by type.</returns>
        /// <remarks>
        /// Output is printed in chat for quick inspection. Only counts are shown, not memory estimates.
        /// </remarks>
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

        /// <summary>
        /// Starts tracking a specific object type's instance count over time.
        /// </summary>
        /// <param name="typeName">The type to track (partial match allowed).</param>
        /// <param name="intervalSec">Interval in seconds to poll and log instance counts.</param>
        /// <returns>Command result confirming watcher registration or error.</returns>
        /// <remarks>
        /// The watcher logs growth trends and stores data for graph export. Multiple types can be watched in parallel.
        /// </remarks>
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

        /// <summary>
        /// Exports a snapshot to a CSV file for use in spreadsheets or analysis.
        /// </summary>
        /// <param name="name">The snapshot name (without extension).</param>
        /// <returns>A command result indicating success or failure.</returns>
        /// <remarks>
        /// CSV format includes type name and instance count, and will be expanded to include tracked IDs in future versions.
        /// </remarks>
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

        /// <summary>
        /// Generates a heatmap-style delta between two snapshots and prints the top growth/shrinkage types.
        /// </summary>
        /// <param name="oldName">Snapshot name to use as the baseline (older).</param>
        /// <param name="newName">Snapshot name to compare against (newer).</param>
        /// <returns>A command result with the summary log output.</returns>
        /// <remarks>
        /// Useful for quickly diagnosing leak-prone or noisy systems over time.
        /// Printed output ranks types by the absolute delta.
        /// </remarks>
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

        /// <summary>
        /// Exports the heatmap-style delta of two snapshots to a CSV file.
        /// </summary>
        /// <param name="oldName">Snapshot name used as the baseline.</param>
        /// <param name="newName">Snapshot name to compare against.</param>
        /// <returns>Success or error message for the CSV export.</returns>
        /// <remarks>
        /// Outputs a CSV with two columns: type name and instance delta. Sorted by largest absolute change.
        /// </remarks>
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

        /// <summary>
        /// Aggregates the top types across N recent snapshots and reports average instance counts.
        /// </summary>
        /// <param name="snapshotCount">Number of recent snapshots to analyze.</param>
        /// <returns>A textual summary of dominant object types.</returns>
        /// <remarks>
        /// This helps reveal persistent high-memory or overused systems.
        /// Shows relative frequency and estimated memory size per type.
        /// </remarks>
        private TextCommandResult CmdSummary(int snapshotCount)
        {
            var files = Directory.GetFiles(snapshotDir, "*.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(snapshotCount)
                .ToList();

            if (files.Count == 0)
                return TextCommandResult.Error("[MemLeakInspector] No snapshots available.");

            var totals = new Dictionary<string, int>();
            var memory = new Dictionary<string, long>();

            foreach (var file in files)
            {
                var json = File.ReadAllText(file);
                var snap = JsonSerializer.Deserialize<MemSnapshot>(json);
                if (snap?.ObjectCountsByType == null) continue;

                foreach (var kvp in snap.ObjectCountsByType)
                {
                    if (!totals.ContainsKey(kvp.Key)) totals[kvp.Key] = 0;
                    totals[kvp.Key] += kvp.Value;
                }

                if (snap.EstimatedMemoryBytesPerType != null)
                {
                    foreach (var kvp in snap.EstimatedMemoryBytesPerType)
                    {
                        if (!memory.ContainsKey(kvp.Key)) memory[kvp.Key] = 0;
                        memory[kvp.Key] += kvp.Value;
                    }
                }
            }
            foreach (var key in totals.Keys.ToList())
            {
                totals[key] /= files.Count;
            }
            if (totals.Count == 0)
                return TextCommandResult.Success("[MemLeakInspector] Summary found no tracked types.");

            sapi.Logger.Notification($"[MemLeakInspector] Summary across {files.Count} snapshots:");

            int max = totals.Max(kv =>  kv.Value);

            foreach (var entry in totals.OrderByDescending(kv => kv.Value).Take(10))
            {
                string bar = AsciiBar(entry.Value, max);
                memory.TryGetValue(entry.Key, out long memBytes);
                double memMB = memBytes / (1024  * 1024);
                sapi.Logger.Notification($"{bar}  {entry.Value,5} x {entry.Key,-60} ≈ {memMB,6:F1} MB");
            }

            return TextCommandResult.Success("[MemLeakInspector] Summary complete.");
        }

        /// <summary>
        /// Exports the count of a specific type across a time-series of snapshots to a CSV file.
        /// </summary>
        /// <param name="typeName">Type name to track over time (partial match allowed).</param>
        /// <param name="limit">Max number of snapshots to analyze (default 20).</param>
        /// <returns>Command result containing export status.</returns>
        /// <remarks>
        /// Useful for graphing leak growth in Excel or plotting tools.
        /// </remarks>
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

            var dataPoints = new List<(DateTime time, int count)>();

            using (var writer = new StreamWriter(exportPath))
            {
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
                    dataPoints.Add((snap.Timestamp, count));

                    writer.WriteLine($"{snap.Timestamp:yyyy-MM-dd HH:mm:ss},{count}");

                    sapi.Logger.Notification($"[MemLeakInspector] Snapshot {Path.GetFileName(file)} matched count: {count}");
                }
            }
            sapi.Logger.Notification($"[MemLeakInspector] Instance history for '{typeName}':");

            int maxCount = dataPoints.Max(dp => dp.count);
            int? previous = null;

            foreach (var (time, count) in dataPoints)
            {
                int delta = previous.HasValue ? (count - previous.Value) : 0;
                string deltaStr = previous.HasValue ? $" ({(delta >= 0 ? "+" : "")}{delta})" : "";
                string bar = AsciiBar(count, maxCount);
                sapi.Logger.Notification($"{time:HH:mm:ss} {bar} {count}{deltaStr}");
                previous = count;
            }

            return TextCommandResult.Success($"[MemLeakInspector] Graph exported to: {Path.GetFileName(exportPath)}");
        }

        /// <summary>
        /// Batch-export graphs for all currently watched types.
        /// </summary>
        /// <param name="limit">Number of recent snapshots to include per graph.</param>
        /// <returns>Success message after generating all graph CSVs.</returns>
        /// <remarks>
        /// Affects all types registered via /mem watch.
        /// </remarks>
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

        /// <summary>
        /// Shows estimated memory usage (in MB) per type based on a snapshot.
        /// </summary>
        /// <param name="snapshotName">Name of the snapshot file (no extension).</param>
        /// <returns>List of memory usage per tracked type.</returns>
        /// <remarks>
        /// Uses cached or inferred type sizes. Results are approximate and should be interpreted accordingly.
        /// </remarks>
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

        /// <summary>
        /// Detects leaking types and sends tracked instance positions to clients for visual highlighting.
        /// </summary>
        /// <returns>Command result with success or error message.</returns>
        /// <remarks>
        /// Requires TrackIndividualEntities to be enabled. Sends a custom packet to all players with highlight data.
        /// </remarks>
        private TextCommandResult CmdShowHeat()
        {
            if (!config!.TrackIndividualEntities)
                return TextCommandResult.Error("[MemLeakInspector] Instance tracking must be enabled.");

            if (lastAlertSnapshot == null)
                return TextCommandResult.Error("[MemLeakInspector] No snapshot history available.");

            var current = TakeSnapshot(config);

            var heat = new List<MemLeakInspectorHighlightPacket.HighlightGroup>();

            foreach (var kv in current.ObjectCountsByType)
            {
                int previous = lastAlertSnapshot.ObjectCountsByType.TryGetValue(kv.Key, out int val) ? val : 0;
                int delta = kv.Value - previous;

                if (delta >= heatThreshold &&
                    current.TrackedInstancesByType?.TryGetValue(kv.Key, out var list) == true)
                {
                    var positions = list.Where(i => i.Pos != null).Select(i => i.Pos!).ToList();
                    heat.Add(new MemLeakInspectorHighlightPacket.HighlightGroup
                    {
                        Type = kv.Key,
                        Positions = positions
                    });
                }
            }

            if (heat.Count == 0)
                return TextCommandResult.Success("[MemLeakInspector] No high-growth types found.");

            var packet = new MemLeakInspectorHighlightPacket
            {
                Highlights = heat
            };

            foreach (var player in sapi.World.AllOnlinePlayers)
            {
                if (player is IServerPlayer serverPlayer)
                {
                    sapi.Network
                        .GetChannel("memleakinspector")
                        .SendPacket(packet, serverPlayer);
                }
            }

            lastAlertSnapshot = current;
            return TextCommandResult.Success($"[MemLeakInspector] Sent heatmap for {heat.Count} leaking types.");
        }

        /// <summary>
        /// Reports the current process threads and their CPU usage.
        /// </summary>
        /// <returns>A list of active thread stats including CPU time and wait state.</returns>
        /// <remarks>
        /// Experimental. Output includes total threads, individual state, and CPU time. Not all data is always accessible.
        /// </remarks>
        private TextCommandResult CmdListThreads()
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var threads = proc.Threads;

            var lines = new List<string>();
            lines.Add($"[MemLeakInspector] Thread snapshot captured at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"Total threads: {threads.Count}");

            foreach (System.Diagnostics.ProcessThread thread in threads)
            {
                string status = $"#{thread.Id}";

                try
                {
                    status += $" State={thread.ThreadState}";

                    if (thread.ThreadState == System.Diagnostics.ThreadState.Wait)
                        status += $" WaitReason={thread.WaitReason}";

                    status += $" TotalCPU={thread.TotalProcessorTime.TotalMilliseconds:0}ms";
                }
                catch
                {
                    status += " [Info not accessible]";
                }

                lines.Add(status);
            }
            string filename = Path.Combine(snapshotDir, $"threads-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllLines(filename, lines);
            return TextCommandResult.Success($"[MemLeakInspector] Exported {threads.Count} threads to: {Path.GetFileName(filename)}");
        }

        private TextCommandResult CmdStartThreadWatch(int intervalSec)
        {
            if (threadWatcherRunning)
                return TextCommandResult.Success("[MemLeakInspector] Thread watcher already running.");

            threadWatcherRunning = true;
            threadWatcherIntervalSec = Math.Max(2, intervalSec); // Minimum 2 sec
            threadWatcherFilename = Path.Combine(snapshotDir, $"threadlog-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            threadWatcherHistory.Clear(); // Reset history if graphing
            sapi.World.RegisterCallback(ThreadWatcherTick, 1000);

            return TextCommandResult.Success($"[MemLeakInspector] Thread watcher started. Interval: {threadWatcherIntervalSec}s");
        }

        private TextCommandResult CmdStopThreadWatch()
        {
            if (!threadWatcherRunning)
                return TextCommandResult.Success("[MemLeakInspector] Thread watcher is not running.");
            if (threadWatcherHistory.Count > 1)
            {
                string summaryPath = Path.Combine(snapshotDir, $"threadgraph-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                var lines = new List<string>();
                int max = threadWatcherHistory.Max(e => e.Count);

                foreach (var entry in threadWatcherHistory)
                {
                    string bar = new string('#', (int)(entry.Count / (float)max * 40));
                    lines.Add($"[{entry.Time:HH:mm:ss}] {entry.Count,3} | {bar}");
                }

                File.WriteAllLines(summaryPath, lines);
                sapi.Logger.Notification($"[MemLeakInspector] Saved thread graph: {Path.GetFileName(summaryPath)}");
            }
            threadWatcherRunning = false;
            return TextCommandResult.Success("[MemLeakInspector] Thread watcher stopped.");
        }


        #endregion

        #region Snapshot Logic

        /// <summary>
        /// Takes a snapshot of all currently tracked instances, optionally including detailed tracked IDs.
        /// </summary>
        /// <param name="config">The mod configuration that controls whether individual instances are tracked.</param>
        /// <returns>A MemSnapshot representing the state of managed memory at the current moment.</returns>
        /// <remarks>
        /// This method performs a full GC cycle before collecting data. The resulting snapshot contains per-type instance counts,
        /// optional ID+position data, and estimated memory usage.
        /// </remarks>
        private static MemSnapshot TakeSnapshot(MemLeakInspectorConfig config)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var counts = InstanceTracker.GetLiveCounts();
            Dictionary<string, List<InstanceTracker.InstanceInfo>>? instanceIds = null;

            if (config.TrackIndividualEntities)
            {
                instanceIds = InstanceTracker.GetInstanceInfoByType();
            }

            var snapshot = new MemSnapshot
            {
                Timestamp = DateTime.UtcNow,
                TotalManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: true),
                ObjectCountsByType = counts,
                TrackedInstancesByType = instanceIds,
                EstimatedBytesPerType = new Dictionary<string, int>(typeSizeCache),
                EstimatedMemoryBytesPerType = counts.ToDictionary(
                    kvp => kvp.Key,
                    kvp => EstimateTypeSize(kvp.Key, kvp.Value)
                )
            };

            Console.WriteLine($"[DEBUG] Snapshot contains {snapshot.ObjectCountsByType.Count} tracked types.");
            return snapshot;
        }

        /// <summary>
        /// Writes a filtered snapshot to disk containing only types that match the given filter string.
        /// </summary>
        /// <param name="typeFilter">Substring to match against type names.</param>
        /// <remarks>
        /// Used internally during heatmap polling to focus snapshots on watched types only.
        /// </remarks>
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

        /// <summary>
        /// Lists all available snapshot files found in the configured snapshot directory.
        /// </summary>
        /// <returns>Command result with filenames listed in chat.</returns>
        /// <remarks>
        /// Each snapshot is saved as a .json file, timestamped and optionally named.
        /// </remarks>
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

        /// <summary>
        /// Checks the current instance count of a watched type and logs any growth or shrinkage.
        /// </summary>
        /// <param name="typeName">The name of the type being polled.</param>
        /// <remarks>
        /// Intended to be triggered on a repeating timer for each watched type.
        /// </remarks>
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

        /// <summary>
        /// Starts an automated snapshotting task that saves the current state on a timed interval.
        /// </summary>
        /// <param name="intervalSec">Interval in seconds between snapshots.</param>
        /// <remarks>
        /// Useful for passive background tracking. Each snapshot is timestamped and saved to disk.
        /// </remarks>
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

        /// <summary>
        /// Begins a 10-second interval watcher to detect types that rapidly increase in instance count.
        /// </summary>
        /// <remarks>
        /// Logs types that exceed the configured threshold. Snapshots and graphs are generated for leak auditing.
        /// </remarks>
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

        /// <summary>
        /// Estimates the total memory usage for a given type based on its field layout and number of instances.
        /// </summary>
        /// <param name="typeName">The fully qualified name of the type to analyze.</param>
        /// <param name="instanceCount">The number of instances to multiply against the estimated size.</param>
        /// <returns>An estimated memory footprint in bytes.</returns>
        /// <remarks>
        /// Tries to reflect on field types to build a composite object size estimate. Falls back to a default size for unknown types.
        /// </remarks>
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

        private void ThreadWatcherTick(float dt)
        {
            if (!threadWatcherRunning) return;

            try
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                var threads = proc.Threads;
                threadWatcherHistory.Add((DateTime.Now, threads.Count));
                var now = DateTime.Now;

                var lines = new List<string>();
                lines.Add($"[{now:HH:mm:ss}] Threads: {threads.Count}");

                foreach (System.Diagnostics.ProcessThread thread in threads)
                {
                    string status = $"  - ID {thread.Id}";
                    try
                    {
                        status += $" | State: {thread.ThreadState}";
                        if (thread.ThreadState == System.Diagnostics.ThreadState.Wait)
                            status += $" | Wait: {thread.WaitReason}";
                        status += $" | CPU: {thread.TotalProcessorTime}";
                    }
                    catch
                    {
                        status += " | [unreadable]";
                    }

                    lines.Add(status);
                }

                File.AppendAllLines(threadWatcherFilename, lines);
            }
            catch (Exception ex)
            {
                sapi.Logger.Warning($"[MemLeakInspector] ThreadWatcher error: {ex.Message}");
            }

            sapi.World.RegisterCallback(ThreadWatcherTick, threadWatcherIntervalSec * 1000);
        }

        /// <summary>
        /// Determines whether the given type name should be ignored from reporting, based on configured fragments.
        /// </summary>
        /// <param name="typeName">The fully qualified type name to test.</param>
        /// <returns>True if the type should be skipped in alerts or reports.</returns>
        private bool IsIgnoredType(string typeName)
        {
            return config!.IgnoreSpikeTypeFragments.Any(fragment =>
                typeName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0
            );
        }

        private class WatchedType
        {
            public string TypeName = "";
            public int IntervalSec = 30;
            public int LastCount = 0;
        }

        /// <summary>
        /// Converts a complex type name into a more readable label.
        /// </summary>
        /// <param name="type">Full type identifier, optionally with domain and code (e.g. EntityDrifter:game:drifter-normal).</param>
        /// <returns>A user-friendly string representation.</returns>
        /// <remarks>
        /// Useful for log output, collapsing namespaced identifiers into readable format.
        /// </remarks>
        private string FormatTypeName(string type)
        {
            // Expected format: "Vintagestory.GameContent.EntityDrifter:game:drifter-normal"
            var parts = type.Split(':');
            if (parts.Length == 3)
            {
                var shortType = parts[1]; // e.g. "game"
                var code = parts[2];      // e.g. "drifter-normal"
                var name = parts[0].Split('.').Last(); // e.g. "EntityDrifter"
                return $"{name} ({shortType}:{code})";
            }

            return type;
        }

        /// <summary>
        /// Generates a simple proportional ASCII bar to visually compare values.
        /// </summary>
        /// <param name="value">Current value to render.</param>
        /// <param name="max">Maximum value in the dataset.</param>
        /// <param name="width">Width of the bar in characters (default 20).</param>
        /// <returns>String like: [####      ]</returns>
        /// <remarks>
        /// This is used in the graph and summary outputs.
        /// </remarks>
        private string AsciiBar(int value, int max, int width = 20)
        {
            int filled = Math.Clamp((int)((double)value / max * width), 0, width);
            return "[" + new string('#', filled).PadRight(width, ' ') + "]";
        }

        #endregion
    }
}