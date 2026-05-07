using MemLeakInspector.Configuration;
using MemLeakInspector.Diagnostics;
using MemLeakInspector.Tracking;
using Vintagestory.API.Server;

namespace MemLeakInspector.Snapshots;

/// <summary>
/// Central service for building, saving, loading, and querying memory snapshots.
/// Owns the snapshot directory, semaphore gate, and "last snapshot" reference.
/// </summary>
internal sealed class SnapshotService
{
    private readonly ICoreServerAPI _sapi;
    private readonly MemLeakInspectorConfig _config;
    private readonly InstanceTracker _tracker;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string SnapshotDir { get; }
    public MemSnapshot? LastSnapshot { get; set; }

    /// <summary>Optional reference to counters — set by ModSystem after construction.</summary>
    public RuntimeCounterListener? Counters { get; set; }

    public SnapshotService(ICoreServerAPI sapi, MemLeakInspectorConfig config,
        InstanceTracker tracker, string snapshotDir)
    {
        _sapi = sapi;
        _config = config;
        _tracker = tracker;
        SnapshotDir = snapshotDir;

        Directory.CreateDirectory(snapshotDir);
    }

    /// <summary>Build a snapshot synchronously (call from background thread).</summary>
    public MemSnapshot Build(string label)
    {
        if (_config.Snapshots.ForceFullGcBeforeSnapshot)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        var typeCounts = _tracker.GetLiveCounts();
        var chunkCounts = _tracker.GetChunkCounts();

        // Use SizeEstimator for per-type memory estimates
        var estimatedBytes = new Dictionary<string, int>(StringComparer.Ordinal);
        var estimatedTotal = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (typeName, count) in typeCounts)
        {
            int perInstance = SizeEstimator.EstimateInstanceSize(typeName);
            estimatedBytes[typeName] = perInstance;
            estimatedTotal[typeName] = (long)perInstance * count;
        }

        // Optional per-instance details
        Dictionary<string, List<InstanceInfo>>? instances = null;
        if (_config.Tracking.TrackIndividualEntities)
        {
            try { instances = _tracker.GetInstanceInfoByType(); }
            catch { /* best-effort */ }
        }

        // Runtime counter data
        double allocRate = 0;
        long workingSet = 0;
        var ctrSnap = Counters?.Snapshot();
        if (ctrSnap is not null)
        {
            if (ctrSnap.TryGetValue("alloc-rate", out var ar)) allocRate = ar.cur;
            if (ctrSnap.TryGetValue("working-set", out var ws)) workingSet = (long)ws.cur;
        }

        var gcInfo = GC.GetGCMemoryInfo();
        return new MemSnapshot
        {
            Version = 2,
            Timestamp = DateTime.UtcNow,
            TotalManagedMemoryBytes = gcInfo.HeapSizeBytes,
            TypeCounts = typeCounts,
            ChunkCounts = chunkCounts,
            EstimatedBytesPerType = estimatedBytes,
            EstimatedMemoryBytesPerType = estimatedTotal,
            TrackedInstancesByType = instances,
            Runtime = new MemSnapshot.GcInfo
            {
                HeapSizeBytes = gcInfo.HeapSizeBytes,
                MemoryLoadBytes = gcInfo.MemoryLoadBytes,
                Gen2Collections = GC.CollectionCount(2),
                AllocRateBytesPerSec = allocRate,
                WorkingSetBytes = workingSet,
            }
        };
    }

    /// <summary>Take a snapshot on a background thread, save, update LastSnapshot.</summary>
    public async Task TakeAndSaveAsync(string name, bool isAuto = false)
    {
        if (!await _gate.WaitAsync(0))
        {
            _sapi.Logger.Notification("[MemLeakInspector] Snapshot already running, skipped.");
            return;
        }

        try
        {
            var snap = await Task.Run(() => Build(name));

            string folder = isAuto
                ? Path.Combine(SnapshotDir, "autosnap")
                : SnapshotDir;
            Directory.CreateDirectory(folder);

            string path = SnapshotStore.Save(folder, name, snap, _config.Snapshots.CompressSnapshots);
            SnapshotStore.EnforceRetention(SnapshotDir, _config.Snapshots.MaxSnapshotsOnDisk);

            LastSnapshot = snap;

            _sapi.Event.EnqueueMainThreadTask(() =>
                _sapi.Logger.Notification($"[MemLeakInspector] Snapshot saved: {path}"),
                "memsnap-ui");
        }
        catch (Exception ex)
        {
            _sapi.Logger.Error($"[MemLeakInspector] Snapshot failed: {ex.Message}");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Resolve a snapshot name to a file path (fuzzy match).</summary>
    public string? FindSnapshot(string name) => SnapshotFinder.Find(SnapshotDir, name);

    /// <summary>Load a snapshot from disk by resolved path.</summary>
    public MemSnapshot? Load(string path) => SnapshotStore.Load(path);

    /// <summary>List all snapshot files (newest first).</summary>
    public List<(DateTime Modified, string RelativeName)> ListAll()
    {
        var rows = new List<(DateTime, string)>();
        AddFilesFrom(SnapshotDir, "", rows);

        string auto = Path.Combine(SnapshotDir, "autosnap");
        if (Directory.Exists(auto))
            AddFilesFrom(auto, "autosnap/", rows);

        rows.Sort((a, b) => b.Item1.CompareTo(a.Item1));
        return rows;
    }

    private static void AddFilesFrom(string dir, string prefix, List<(DateTime, string)> rows)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "*.json*"))
            rows.Add((File.GetLastWriteTimeUtc(f), prefix + Path.GetFileName(f)));
    }
}
