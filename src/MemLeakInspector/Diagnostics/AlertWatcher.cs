using MemLeakInspector.Configuration;
using MemLeakInspector.Snapshots;
using Vintagestory.API.Server;

namespace MemLeakInspector.Diagnostics;

/// <summary>
/// Periodically compares snapshots and warns when memory or instance-count
/// deltas exceed configured thresholds. Runs entirely on a background tick.
/// </summary>
internal sealed class AlertWatcher
{
    private readonly ICoreServerAPI _sapi;
    private readonly MemLeakInspectorConfig _config;
    private readonly SnapshotService _snapService;

    private MemSnapshot? _baseline;
    private long? _listenerId;
    private bool _snapping;

    public bool IsRunning => _listenerId.HasValue;

    public AlertWatcher(ICoreServerAPI sapi, MemLeakInspectorConfig config, SnapshotService snapService)
    {
        _sapi = sapi;
        _config = config;
        _snapService = snapService;
    }

    public string Start()
    {
        if (_listenerId.HasValue)
            return "[MemLeakInspector] Alert watcher is already running.";

        _baseline = _snapService.Build("alert-baseline");

        _listenerId = _sapi.Event.RegisterGameTickListener(_ => CheckTick(),
            _config.Alerts.CheckIntervalSec * 1000);

        return "[MemLeakInspector] Alert watcher started.";
    }

    public string Stop()
    {
        if (!_listenerId.HasValue)
            return "[MemLeakInspector] No alert watcher running.";

        _sapi.Event.UnregisterGameTickListener(_listenerId.Value);
        _listenerId = null;
        return "[MemLeakInspector] Alert watcher stopped.";
    }

    private void CheckTick()
    {
        if (_snapping) return;
        _snapping = true;

        Task.Run(() =>
        {
            try
            {
                var current = _snapService.Build("alert-check");

                if (_baseline is null) { _baseline = current; return; }

                // Memory spike check
                long memDelta = current.TotalManagedMemoryBytes - _baseline.TotalManagedMemoryBytes;
                long threshold = (long)(_config.Alerts.MemorySpikeMB * 1024 * 1024);
                if (memDelta >= threshold)
                    _sapi.Logger.Warning($"[MemLeakInspector] MEMORY SPIKE: +{memDelta / (1024 * 1024)} MB");

                // Instance spike check
                var ignoreFrags = _config.Alerts.IgnoreSpikeTypeFragments;
                foreach (var key in current.TypeCounts.Keys)
                {
                    if (ignoreFrags.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    int old = _baseline.TypeCounts.GetValueOrDefault(key);
                    int now = current.TypeCounts[key];
                    int delta = now - old;

                    if (delta >= _config.Alerts.InstanceSpike)
                        _sapi.Logger.Warning(
                            $"[MemLeakInspector] INSTANCE SPIKE: {key} grew by {delta} ({old} → {now})");
                }

                _baseline = current;
            }
            catch (Exception ex)
            {
                _sapi.Logger.Error($"[MemLeakInspector] Alert check failed: {ex.Message}");
            }
            finally { _snapping = false; }
        });
    }
}
