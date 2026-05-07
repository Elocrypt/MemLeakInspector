using System.Text.Json.Serialization;
using MemLeakInspector.Tracking;

namespace MemLeakInspector.Snapshots;

/// <summary>
/// Point-in-time snapshot of tracked instances, chunk density, GC state,
/// and optional per-instance metadata.
/// </summary>
public class MemSnapshot
{
    /// <summary>Schema version for forward/backward compat. Bump on breaking layout changes.</summary>
    public int Version { get; set; } = 2;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public long TotalManagedMemoryBytes { get; set; }

    public Dictionary<string, int> TypeCounts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Backward-compat alias used by 1.x code.</summary>
    [JsonIgnore]
    public Dictionary<string, int> ObjectCountsByType
    {
        get => TypeCounts;
        set => TypeCounts = value ?? new(StringComparer.Ordinal);
    }

    public Dictionary<long, int> ChunkCounts { get; set; } = [];

    /// <summary>Estimated per-instance size cache (int, for 1.x compat).</summary>
    public Dictionary<string, int> EstimatedBytesPerType { get; set; } = [];

    /// <summary>Total estimated memory per type (count × per-instance estimate).</summary>
    public Dictionary<string, long> EstimatedMemoryBytesPerType { get; set; } = [];

    /// <summary>Optional tracked instance details (positions, IDs).</summary>
    public Dictionary<string, List<InstanceInfo>>? TrackedInstancesByType { get; set; }

    public GcInfo Runtime { get; set; } = new();

    public sealed class GcInfo
    {
        public long HeapSizeBytes { get; set; }
        public long HighMemoryLoadThresholdBytes { get; set; }
        public long TotalAvailableMemoryBytes { get; set; }
        public int FragmentationPercent { get; set; }
        public long MemoryLoadBytes { get; set; }
        public int Gen2Collections { get; set; }
        public double AllocRateBytesPerSec { get; set; }
        public long WorkingSetBytes { get; set; }
    }
}

/// <summary>Computes deltas between two snapshots.</summary>
public static class SnapshotDiff
{
    public static Dictionary<string, int> DiffTypes(MemSnapshot a, MemSnapshot b)
    {
        var keys = new HashSet<string>(a.TypeCounts.Keys);
        keys.UnionWith(b.TypeCounts.Keys);
        var res = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in keys)
        {
            a.TypeCounts.TryGetValue(k, out var av);
            b.TypeCounts.TryGetValue(k, out var bv);
            int d = bv - av;
            if (d != 0) res[k] = d;
        }
        return res;
    }

    public static Dictionary<long, int> DiffChunks(MemSnapshot a, MemSnapshot b)
    {
        var keys = new HashSet<long>(a.ChunkCounts.Keys);
        keys.UnionWith(b.ChunkCounts.Keys);
        var res = new Dictionary<long, int>();
        foreach (var k in keys)
        {
            a.ChunkCounts.TryGetValue(k, out var av);
            b.ChunkCounts.TryGetValue(k, out var bv);
            int d = bv - av;
            if (d != 0) res[k] = d;
        }
        return res;
    }
}
