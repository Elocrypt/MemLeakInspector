using MemLeakInspector.Commands;
using MemLeakInspector.Configuration;
using MemLeakInspector.Diagnostics;
using MemLeakInspector.Rendering;
using MemLeakInspector.Snapshots;
using MemLeakInspector.Tracking;
using MemLeakInspector.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace MemLeakInspector.Core;

/// <summary>
/// Server-side entry point for MemLeakInspector 2.x.
/// Thin shell: loads config, creates services, wires them, tears down on dispose.
/// </summary>
public class MemLeakInspectorModSystem : ModSystem
{
    private const string ConfigFile = "MemLeakInspectorConfig.json";

    private ICoreServerAPI _sapi = null!;
    private MemLeakInspectorConfig _config = null!;
    private InstanceTracker? _tracker;
    private Harmony.HarmonyManager? _harmony;
    private RuntimeCounterListener? _counters;
    private HeatHighlighter? _highlighter;
    private ThreadMonitor? _threadMonitor;
    private AlertWatcher? _alertWatcher;
    private SnapshotService? _snapService;
    private CommandRouter? _commands;
    private long? _entityPollId;
    private long? _sweepId;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;

        // 1. Coordinate system
        Coords.Init(api);
        api.Logger.Notification(
            $"[MemLeakInspector] HUD center: ({Coords.Center.X}, {Coords.Center.Z})");

        // 2. Configuration
        _config = api.LoadModConfig<MemLeakInspectorConfig>(ConfigFile)
                  ?? new MemLeakInspectorConfig();
        _config.Normalize();
        api.StoreModConfig(_config, ConfigFile);

        // 3. Instance tracker
        _tracker = new InstanceTracker();
        _tracker.SetFilter(_config.Tracking);
        InstanceTracker.Current = _tracker;

        // 4. Harmony patches
        _harmony = new Harmony.HarmonyManager(api);
        _harmony.Apply();

        // 5. Block entity class
        api.RegisterBlockEntityClass("memtrackedbe", typeof(AutoTrackedBE));

        // 6. Snapshot service
        string snapshotDir = Path.Combine(
            api.GetOrCreateDataPath("MemLeakInspector"), "snapshots");
        _snapService = new SnapshotService(api, _config, _tracker, snapshotDir);

        // 7. Optional subsystems
        if (_config.Runtime.Enabled)
            _counters = new RuntimeCounterListener();

        _snapService.Counters = _counters; // may be null if disabled

        if (_config.Heat.Enabled)
            _highlighter = new HeatHighlighter(api,
                _config.Heat.CooldownSec,
                _config.Heat.MaxDistance,
                _config.Heat.TopChunks);

        _threadMonitor = new ThreadMonitor(_config.Threads);
        _alertWatcher = new AlertWatcher(api, _config, _snapService);

        // 8. Auto-start features
        if (_config.Threads.AutoStart)
        {
            _threadMonitor.Start(api, snapshotDir);
            api.Logger.Notification("[MemLeakInspector] Auto-started thread watcher.");
        }

        // 9. Periodic entity re-scan (catches entities loaded outside Harmony path)
        _entityPollId = api.Event.RegisterGameTickListener(
            _ => PollLoadedEntities(), 10_000);

        // 10. Amortized tracker sweep (prune dead weak-refs in background batches)
        _sweepId = api.Event.RegisterGameTickListener(
            _ => _tracker?.SweepBatch(), 5_000);

        // 11. Command registration (last — all services ready)
        _commands = new CommandRouter(
            api, _config, _tracker, _snapService,
            _threadMonitor, _alertWatcher, _counters, _highlighter);
        _commands.Register();

        api.Logger.Notification("[MemLeakInspector] v2.x initialized.");
    }

    public override void Dispose()
    {
        if (_sweepId.HasValue)
        {
            _sapi.Event.UnregisterGameTickListener(_sweepId.Value);
            _sweepId = null;
        }
        if (_entityPollId.HasValue)
        {
            _sapi.Event.UnregisterGameTickListener(_entityPollId.Value);
            _entityPollId = null;
        }

        _alertWatcher?.Stop();
        _threadMonitor?.Stop();
        _counters?.Dispose();
        _harmony?.Dispose();
        _tracker?.Dispose();
        SizeEstimator.ClearCache();
        Coords.Reset();

        base.Dispose();
        _sapi?.Logger.Notification("[MemLeakInspector] Unloaded.");
    }

    private void PollLoadedEntities()
    {
        if (_tracker is null) return;
        foreach (var entity in _sapi.World.LoadedEntities.Values)
            _tracker.Register(entity);
    }
}
