namespace MemLeakInspector.Configuration;

/// <summary>
/// Thresholds and intervals for the automatic spike-detection watcher.
/// </summary>
internal sealed class AlertOptions
{
    /// <summary>Minimum memory delta (MB) that triggers a warning.</summary>
    public double MemorySpikeMB { get; set; } = 100.0;

    /// <summary>Minimum instance-count delta per type that triggers a warning.</summary>
    public int InstanceSpike { get; set; } = 500;

    /// <summary>Seconds between alert-watcher checks.</summary>
    public int CheckIntervalSec { get; set; } = 30;

    /// <summary>Type-name fragments to ignore when evaluating spikes (e.g. "particle", "smoke").</summary>
    public List<string> IgnoreSpikeTypeFragments { get; set; } =
    [
        "butterfly",
        "transient",
        "smoke",
        "sparks",
        "pollen"
    ];
}
