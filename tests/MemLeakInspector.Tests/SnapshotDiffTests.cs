using MemLeakInspector.Snapshots;
using Xunit;

namespace MemLeakInspector.Tests;

public class SnapshotDiffTests
{
    [Fact]
    public void DiffTypes_DetectsGrowth()
    {
        var a = new MemSnapshot
        {
            TypeCounts = new() { ["TypeA"] = 10, ["TypeB"] = 5 }
        };
        var b = new MemSnapshot
        {
            TypeCounts = new() { ["TypeA"] = 15, ["TypeB"] = 5, ["TypeC"] = 3 }
        };

        var diff = SnapshotDiff.DiffTypes(a, b);

        Assert.Equal(5, diff["TypeA"]);    // grew
        Assert.False(diff.ContainsKey("TypeB")); // unchanged
        Assert.Equal(3, diff["TypeC"]);    // new type
    }

    [Fact]
    public void DiffTypes_DetectsShrinkage()
    {
        var a = new MemSnapshot
        {
            TypeCounts = new() { ["TypeA"] = 20 }
        };
        var b = new MemSnapshot
        {
            TypeCounts = new() { ["TypeA"] = 5 }
        };

        var diff = SnapshotDiff.DiffTypes(a, b);
        Assert.Equal(-15, diff["TypeA"]);
    }

    [Fact]
    public void DiffTypes_DetectsRemoval()
    {
        var a = new MemSnapshot
        {
            TypeCounts = new() { ["TypeA"] = 10, ["TypeB"] = 5 }
        };
        var b = new MemSnapshot
        {
            TypeCounts = new() { ["TypeA"] = 10 }
        };

        var diff = SnapshotDiff.DiffTypes(a, b);
        Assert.False(diff.ContainsKey("TypeA")); // unchanged
        Assert.Equal(-5, diff["TypeB"]);           // removed
    }

    [Fact]
    public void DiffChunks_ComputesDeltas()
    {
        var a = new MemSnapshot
        {
            ChunkCounts = new() { [100L] = 10, [200L] = 5 }
        };
        var b = new MemSnapshot
        {
            ChunkCounts = new() { [100L] = 20, [300L] = 3 }
        };

        var diff = SnapshotDiff.DiffChunks(a, b);
        Assert.Equal(10, diff[100L]);
        Assert.Equal(-5, diff[200L]);
        Assert.Equal(3, diff[300L]);
    }

    [Fact]
    public void EmptySnapshots_ProduceEmptyDiff()
    {
        var a = new MemSnapshot();
        var b = new MemSnapshot();

        Assert.Empty(SnapshotDiff.DiffTypes(a, b));
        Assert.Empty(SnapshotDiff.DiffChunks(a, b));
    }
}
