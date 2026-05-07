using MemLeakInspector.Tracking;
using Xunit;

namespace MemLeakInspector.Tests;

public class TrackingFilterTests
{
    [Fact]
    public void EmptyFilter_AllowsEverything()
    {
        var filter = new TrackingFilter();
        Assert.True(filter.IsAllowed(typeof(string)));
        Assert.True(filter.IsAllowed(typeof(int)));
    }

    [Fact]
    public void DenyList_BlocksMatchingTypes()
    {
        var filter = new TrackingFilter([], ["System\\.String"]);
        Assert.False(filter.IsAllowed(typeof(string)));
        Assert.True(filter.IsAllowed(typeof(int)));
    }

    [Fact]
    public void AllowList_OnlyAllowsMatching()
    {
        var filter = new TrackingFilter(["System\\.Int"], []);
        Assert.True(filter.IsAllowed(typeof(int)));
        Assert.True(filter.IsAllowed(typeof(Int64))); // System.Int64
        Assert.False(filter.IsAllowed(typeof(string)));
    }

    [Fact]
    public void DenyTakesPriority_OverAllow()
    {
        var filter = new TrackingFilter(["System\\..*"], ["System\\.String"]);
        Assert.False(filter.IsAllowed(typeof(string))); // denied
        Assert.True(filter.IsAllowed(typeof(int)));       // allowed
    }

    [Fact]
    public void BlankPatterns_AreIgnored()
    {
        var filter = new TrackingFilter(["", "  "], ["", null!]);
        Assert.True(filter.IsAllowed(typeof(string)));
    }
}
