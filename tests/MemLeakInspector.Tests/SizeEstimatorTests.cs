using MemLeakInspector.Tracking;
using Xunit;

namespace MemLeakInspector.Tests;

public class SizeEstimatorTests
{
    public SizeEstimatorTests()
    {
        SizeEstimator.ClearCache();
    }

    [Fact]
    public void KnownType_ReturnsReasonableSize()
    {
        int size = SizeEstimator.EstimateInstanceSize(typeof(string).FullName!);
        Assert.True(size > 0);
        Assert.True(size < 10_000); // shouldn't be absurdly large
    }

    [Fact]
    public void UnknownType_ReturnsFallback()
    {
        int size = SizeEstimator.EstimateInstanceSize("Some.Nonexistent.Type.Name");
        Assert.Equal(300, size); // fallback
    }

    [Fact]
    public void EstimateTotal_MultipliesCorrectly()
    {
        int perInstance = SizeEstimator.EstimateInstanceSize(typeof(int).FullName!);
        long total = SizeEstimator.EstimateTotal(typeof(int).FullName!, 100);
        Assert.Equal((long)perInstance * 100, total);
    }

    [Fact]
    public void Seed_PopulatesCache()
    {
        SizeEstimator.Seed(new Dictionary<string, int> { ["Custom.Type"] = 42 });
        Assert.Equal(42, SizeEstimator.EstimateInstanceSize("Custom.Type"));
    }

    [Fact]
    public void ExportCache_ContainsCachedEntries()
    {
        _ = SizeEstimator.EstimateInstanceSize(typeof(double).FullName!);
        var cache = SizeEstimator.ExportCache();
        Assert.True(cache.ContainsKey(typeof(double).FullName!));
    }

    [Fact]
    public void ResultIsCached_SecondCallFast()
    {
        string type = typeof(List<int>).FullName!;
        int first = SizeEstimator.EstimateInstanceSize(type);
        int second = SizeEstimator.EstimateInstanceSize(type);
        Assert.Equal(first, second);
    }
}
