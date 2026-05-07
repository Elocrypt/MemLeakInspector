using MemLeakInspector.Configuration;
using MemLeakInspector.Tracking;
using Xunit;

namespace MemLeakInspector.Tests;

public class InstanceTrackerTests : IDisposable
{
    private readonly InstanceTracker _tracker;

    public InstanceTrackerTests()
    {
        _tracker = new InstanceTracker();
        _tracker.SetFilter(new TrackingOptions()); // default: allow all
        InstanceTracker.Current = _tracker;
    }

    public void Dispose()
    {
        _tracker.Dispose();
    }

    [Fact]
    public void Register_TracksObject()
    {
        var obj = new TestObject();
        _tracker.Register(obj);

        var counts = _tracker.GetLiveCounts();
        Assert.True(counts.ContainsKey(typeof(TestObject).FullName!));
        Assert.Equal(1, counts[typeof(TestObject).FullName!]);
    }

    [Fact]
    public void Register_IgnoresDuplicates()
    {
        var obj = new TestObject();
        _tracker.Register(obj);
        _tracker.Register(obj); // same instance
        _tracker.Register(obj);

        var counts = _tracker.GetLiveCounts();
        Assert.Equal(1, counts[typeof(TestObject).FullName!]);
    }

    [Fact]
    public void Register_IgnoresNull()
    {
        _tracker.Register(null!);
        Assert.Empty(_tracker.GetLiveCounts());
    }

    [Fact]
    public void Register_MultipleTypes()
    {
        _tracker.Register(new TestObject());
        _tracker.Register(new AnotherTestObject());
        _tracker.Register(new AnotherTestObject());

        var counts = _tracker.GetLiveCounts();
        Assert.Equal(1, counts[typeof(TestObject).FullName!]);
        Assert.Equal(2, counts[typeof(AnotherTestObject).FullName!]);
    }

    [Fact]
    public void DeadReferences_ArePruned()
    {
        RegisterAndAbandon();

        // Force GC to collect the abandoned object
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var counts = _tracker.GetLiveCounts();
        // The abandoned object's type should have 0 live refs (absent from dict)
        Assert.False(counts.ContainsKey(typeof(TestObject).FullName!));
    }

    [Fact]
    public void Clear_RemovesAllState()
    {
        _tracker.Register(new TestObject());
        Assert.NotEmpty(_tracker.GetLiveCounts());

        _tracker.Clear();
        Assert.Empty(_tracker.GetLiveCounts());
        Assert.Equal(0, _tracker.TrackedTypeCount);
    }

    [Fact]
    public void DenyFilter_PreventsTracking()
    {
        _tracker.SetFilter(new TrackingOptions
        {
            DenyListRegex = [nameof(TestObject)]
        });

        _tracker.Register(new TestObject());
        Assert.Empty(_tracker.GetLiveCounts());
    }

    [Fact]
    public void AllowFilter_RestrictsTracking()
    {
        _tracker.SetFilter(new TrackingOptions
        {
            AllowListRegex = [nameof(AnotherTestObject)]
        });

        _tracker.Register(new TestObject());
        _tracker.Register(new AnotherTestObject());

        var counts = _tracker.GetLiveCounts();
        Assert.False(counts.ContainsKey(typeof(TestObject).FullName!));
        Assert.True(counts.ContainsKey(typeof(AnotherTestObject).FullName!));
    }

    [Fact]
    public void SweepBatch_PrunesEmptyBuckets()
    {
        RegisterAndAbandon();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Before sweep, the type key may still exist (just with dead refs)
        _tracker.SweepBatch();

        // After sweep + prune, empty bucket should be removed
        Assert.Equal(0, _tracker.TrackedTypeCount);
    }

    [Fact]
    public void GetLiveObjects_ReturnsActualInstances()
    {
        var obj = new TestObject { Tag = "hello" };
        _tracker.Register(obj);

        var live = _tracker.GetLiveObjects();
        Assert.True(live.ContainsKey(typeof(TestObject).FullName!));
        var instance = Assert.Single(live[typeof(TestObject).FullName!]);
        Assert.Equal("hello", ((TestObject)instance).Tag);
    }

    [Fact]
    public void Dispose_NullsCurrentReference()
    {
        Assert.Same(_tracker, InstanceTracker.Current);
        _tracker.Dispose();
        Assert.Null(InstanceTracker.Current);
    }

    // -- Helpers --

    // Register an object with no surviving reference so GC can collect it
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void RegisterAndAbandon()
    {
        _tracker.Register(new TestObject());
    }

    private class TestObject
    {
        public string Tag { get; set; } = "";
    }

    private class AnotherTestObject { }
}
