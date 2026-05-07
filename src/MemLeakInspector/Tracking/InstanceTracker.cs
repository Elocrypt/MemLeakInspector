using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MemLeakInspector.Configuration;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace MemLeakInspector.Tracking;

/// <summary>
/// Tracks live instances using weak references so GC is not affected.
/// Maintains per-type counts and per-chunk density for heat overlays.
/// </summary>
/// <remarks>
/// <para>Instance-based for testability. Harmony patches reach the active
/// instance via <see cref="Current"/>.</para>
/// <para><b>Pruning strategy:</b> Dead weak-refs are pruned lazily when a
/// type's list is read. A periodic amortized sweep prunes a fraction of
/// types each call to keep lists bounded even for unqueried types.</para>
/// </remarks>
internal sealed class InstanceTracker : IDisposable
{
    internal static InstanceTracker? Current { get; set; }

    private readonly ConcurrentDictionary<string, List<WeakReference<object>>> _perType = new();
    private readonly ConcurrentDictionary<long, int> _chunkCounts = new();
    private ConditionalWeakTable<object, object?> _seen = new();
    private TrackingFilter _filter = new();

    private int _sweepCursor;
    private const int SweepBatchSize = 8;

    // -- Configuration --

    public void SetFilter(TrackingOptions opts)
        => _filter = new TrackingFilter(opts.AllowListRegex, opts.DenyListRegex);

    // -- Registration --

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Register(object obj)
    {
        if (obj is null) return;
        if (_seen.TryGetValue(obj, out _)) return;
        _seen.Add(obj, null);

        var type = obj.GetType();
        if (!_filter.IsAllowed(type)) return;

        string key = type.FullName ?? type.Name;
        var list = _perType.GetOrAdd(key, _ => new List<WeakReference<object>>());
        lock (list) list.Add(new WeakReference<object>(obj));

        TryRecordChunk(obj);
    }

    public void Unregister(object obj) { /* no-op — weak refs expire naturally */ }

    // -- Queries --

    public Dictionary<string, int> GetLiveCounts()
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in _perType)
        {
            int alive = PruneAndCount(kv.Value);
            if (alive > 0) result[kv.Key] = alive;
        }
        return result;
    }

    public Dictionary<string, List<object>> GetLiveObjects()
    {
        var result = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        foreach (var kv in _perType)
        {
            var live = PruneAndCollect(kv.Value);
            if (live.Count > 0) result[kv.Key] = live;
        }
        return result;
    }

    public Dictionary<long, int> GetChunkCounts()
        => _chunkCounts.ToDictionary(k => k.Key, v => v.Value);

    public BlockPos? FindPositionById(string id)
    {
        foreach (var infos in GetInstanceInfoByType().Values)
            foreach (var info in infos)
                if (info.Id == id || info.Id.StartsWith(id, StringComparison.Ordinal))
                    return info.Pos;
        return null;
    }

    public Dictionary<string, List<InstanceInfo>> GetInstanceInfoByType()
    {
        var result = new Dictionary<string, List<InstanceInfo>>(StringComparer.Ordinal);
        foreach (var (typeName, objects) in GetLiveObjects())
        {
            var list = new List<InstanceInfo>(objects.Count);
            foreach (var obj in objects)
                list.Add(InstanceInfo.FromObject(obj));
            if (list.Count > 0)
                result[typeName] = list;
        }
        return result;
    }

    public int TrackedTypeCount => _perType.Count;

    // -- Amortized sweep --

    /// <summary>
    /// Prune a batch of type buckets to reclaim dead weak refs.
    /// Call periodically from a game tick listener.
    /// </summary>
    public void SweepBatch()
    {
        var keys = _perType.Keys.ToArray();
        if (keys.Length == 0) return;

        int start = _sweepCursor % keys.Length;
        int count = Math.Min(SweepBatchSize, keys.Length);

        for (int i = 0; i < count; i++)
        {
            int idx = (start + i) % keys.Length;
            if (_perType.TryGetValue(keys[idx], out var list))
            {
                int alive = PruneAndCount(list);
                if (alive == 0) _perType.TryRemove(keys[idx], out _);
            }
        }

        _sweepCursor = (start + count) % Math.Max(1, keys.Length);
    }

    // -- Lifecycle --

    public void Clear()
    {
        _perType.Clear();
        _chunkCounts.Clear();
        _seen = new ConditionalWeakTable<object, object?>();
        _sweepCursor = 0;
    }

    public void Dispose()
    {
        Clear();
        if (Current == this) Current = null;
    }

    // -- Internal helpers --

    private static int PruneAndCount(List<WeakReference<object>> list)
    {
        lock (list)
        {
            int w = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].TryGetTarget(out _))
                    list[w++] = list[i];
            if (w < list.Count) list.RemoveRange(w, list.Count - w);
            return w;
        }
    }

    private static List<object> PruneAndCollect(List<WeakReference<object>> list)
    {
        var live = new List<object>();
        lock (list)
        {
            int w = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].TryGetTarget(out var target))
                {
                    live.Add(target);
                    list[w++] = list[i];
                }
            if (w < list.Count) list.RemoveRange(w, list.Count - w);
        }
        return live;
    }

    private void TryRecordChunk(object obj)
    {
        try
        {
            BlockPos? pos = obj switch
            {
                Entity e => e.Pos?.AsBlockPos,
                BlockEntity be => be.Pos,
                _ => null
            };
            if (pos is null) return;
            long key = PackChunkKey(pos);
            _chunkCounts.AddOrUpdate(key, 1, (_, v) => v + 1);
        }
        catch { /* best effort */ }
    }

    private static long PackChunkKey(BlockPos p)
    {
        int cx = p.X / 32;
        int cy = p.Y / 32;
        int cz = p.Z / 32;
        return (((long)cy) << 32) ^ (((long)cx) << 16) ^ (uint)cz;
    }
}
