using MemLeakInspector.Configuration;
using Xunit;

namespace MemLeakInspector.Tests;

public class ConfigTests
{
    [Fact]
    public void Normalize_ClampsMinimumValues()
    {
        var config = new MemLeakInspectorConfig
        {
            Alerts = new AlertOptions { CheckIntervalSec = 1 },
            Threads = new ThreadOptions { IntervalSec = 0, MaxHistory = -5 },
            Snapshots = new SnapshotOptions { MaxSnapshotsOnDisk = 0, DiffPreviewLines = 0 },
            Heat = new HeatOptions { CooldownSec = 0, MaxDistance = 1, TopChunks = 0 }
        };

        config.Normalize();

        Assert.True(config.Alerts.CheckIntervalSec >= 5);
        Assert.True(config.Threads.IntervalSec >= 2);
        Assert.True(config.Threads.MaxHistory >= 1);
        Assert.True(config.Snapshots.MaxSnapshotsOnDisk >= 1);
        Assert.True(config.Snapshots.DiffPreviewLines >= 1);
        Assert.True(config.Heat.CooldownSec >= 1);
        Assert.True(config.Heat.MaxDistance >= 16);
        Assert.True(config.Heat.TopChunks >= 1);
    }

    [Fact]
    public void Normalize_DeduplicatesIgnoreFragments()
    {
        var config = new MemLeakInspectorConfig
        {
            Alerts = new AlertOptions
            {
                IgnoreSpikeTypeFragments = ["butterfly", "BUTTERFLY", "smoke", "smoke"]
            }
        };

        config.Normalize();

        Assert.Equal(2, config.Alerts.IgnoreSpikeTypeFragments.Count);
    }

    [Fact]
    public void DefaultConfig_HasSaneDefaults()
    {
        var config = new MemLeakInspectorConfig();

        Assert.True(config.EnableAsyncCommands);
        Assert.True(config.Snapshots.CompressSnapshots);
        Assert.True(config.Tracking.TrackIndividualEntities);
        Assert.True(config.Heat.Enabled);
        Assert.True(config.Runtime.Enabled);
        Assert.Equal(200, config.Snapshots.MaxSnapshotsOnDisk);
        Assert.False(config.Threads.AutoStart);
    }

    [Fact]
    public void HeatOptions_TopChunks_ClampedToMax()
    {
        var config = new MemLeakInspectorConfig
        {
            Heat = new HeatOptions { TopChunks = 9999 }
        };
        config.Normalize();
        Assert.True(config.Heat.TopChunks <= 1024);
    }
}
