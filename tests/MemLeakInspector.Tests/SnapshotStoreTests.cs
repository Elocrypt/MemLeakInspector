using MemLeakInspector.Snapshots;
using Xunit;

namespace MemLeakInspector.Tests;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _dir;

    public SnapshotStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mli-store-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveAndLoad_Uncompressed_RoundTrips()
    {
        var snap = MakeSnapshot();
        string path = SnapshotStore.Save(_dir, "test", snap, compress: false);

        Assert.True(File.Exists(path));
        Assert.EndsWith(".json", path);

        var loaded = SnapshotStore.Load(path);
        Assert.NotNull(loaded);
        Assert.Equal(snap.TypeCounts.Count, loaded!.TypeCounts.Count);
        Assert.Equal(snap.TypeCounts["TypeA"], loaded.TypeCounts["TypeA"]);
        Assert.Equal(snap.TotalManagedMemoryBytes, loaded.TotalManagedMemoryBytes);
    }

    [Fact]
    public void SaveAndLoad_Compressed_RoundTrips()
    {
        var snap = MakeSnapshot();
        string path = SnapshotStore.Save(_dir, "testgz", snap, compress: true);

        Assert.True(File.Exists(path));
        Assert.EndsWith(".json.gz", path);

        var loaded = SnapshotStore.Load(path);
        Assert.NotNull(loaded);
        Assert.Equal(snap.TypeCounts["TypeB"], loaded!.TypeCounts["TypeB"]);
    }

    [Fact]
    public void EnforceRetention_DeletesOldest()
    {
        // Create 5 files with staggered creation times
        for (int i = 0; i < 5; i++)
        {
            string p = Path.Combine(_dir, $"snap{i}.json");
            File.WriteAllText(p, "{}");
            File.SetCreationTimeUtc(p, DateTime.UtcNow.AddMinutes(-5 + i));
            Thread.Sleep(10);
        }

        SnapshotStore.EnforceRetention(_dir, 3);

        var remaining = Directory.GetFiles(_dir, "*.json");
        Assert.Equal(3, remaining.Length);
        // Oldest (snap0, snap1) should be gone
        Assert.DoesNotContain(remaining, f => Path.GetFileName(f) == "snap0.json");
    }

    [Fact]
    public void Load_ReturnsNull_ForMissingFile()
    {
        var result = SnapshotStore.Load(Path.Combine(_dir, "nope.json"));
        Assert.Null(result);
    }

    [Fact]
    public void Version2_IsSerializedCorrectly()
    {
        var snap = MakeSnapshot();
        string path = SnapshotStore.Save(_dir, "v2test", snap, compress: false);
        var loaded = SnapshotStore.Load(path);
        Assert.Equal(2, loaded!.Version);
    }

    private static MemSnapshot MakeSnapshot() => new()
    {
        Version = 2,
        Timestamp = DateTime.UtcNow,
        TotalManagedMemoryBytes = 123_456_789,
        TypeCounts = new() { ["TypeA"] = 100, ["TypeB"] = 50 },
        ChunkCounts = new() { [42L] = 7 },
        EstimatedBytesPerType = new() { ["TypeA"] = 200, ["TypeB"] = 150 },
        EstimatedMemoryBytesPerType = new() { ["TypeA"] = 20_000, ["TypeB"] = 7_500 },
    };
}
