namespace MemLeakInspector.Configuration;

/// <summary>
/// Root configuration for MemLeakInspector. Aggregates feature-specific option sections.
/// Serialized to / loaded from <c>MemLeakInspectorConfig.json</c> via the VS mod config API.
/// </summary>
/// <remarks>
/// <para><b>Migration:</b> The 1.x config stored everything flat. When the VS config loader
/// deserializes an old file, the section objects will get their defaults while any unrecognized
/// top-level keys are silently ignored. On the first <c>StoreModConfig</c> call the file is
/// rewritten in the new sectioned format — effectively a one-way migration.</para>
/// </remarks>
internal sealed class MemLeakInspectorConfig
{
    // -----------------------------------------------------------------
    //  Feature sections
    // -----------------------------------------------------------------

    public SnapshotOptions Snapshots { get; set; } = new();
    public TrackingOptions Tracking { get; set; } = new();
    public AlertOptions Alerts { get; set; } = new();
    public ThreadOptions Threads { get; set; } = new();
    public HeatOptions Heat { get; set; } = new();
    public RuntimeOptions Runtime { get; set; } = new();

    // -----------------------------------------------------------------
    //  Global settings
    // -----------------------------------------------------------------

    /// <summary>
    /// Offload heavy commands (diff, report, heatmap) to a background thread.
    /// Disable only for debugging.
    /// </summary>
    public bool EnableAsyncCommands { get; set; } = true;

    /// <summary>
    /// Minimum memory (MB) a type must consume to appear in memory-usage reports.
    /// </summary>
    public double ReportFilterMB { get; set; }

    // -----------------------------------------------------------------
    //  Post-load normalization
    // -----------------------------------------------------------------

    /// <summary>
    /// Called after deserialization to clamp values, deduplicate lists, etc.
    /// </summary>
    internal void Normalize()
    {
        Alerts.IgnoreSpikeTypeFragments = Alerts.IgnoreSpikeTypeFragments
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Alerts.CheckIntervalSec = Math.Max(5, Alerts.CheckIntervalSec);
        Threads.IntervalSec = Math.Max(2, Threads.IntervalSec);
        Threads.MaxHistory = Math.Max(1, Threads.MaxHistory);
        Snapshots.MaxSnapshotsOnDisk = Math.Max(1, Snapshots.MaxSnapshotsOnDisk);
        Snapshots.DiffPreviewLines = Math.Max(1, Snapshots.DiffPreviewLines);
        Heat.CooldownSec = Math.Max(1, Heat.CooldownSec);
        Heat.MaxDistance = Math.Max(16, Heat.MaxDistance);
        Heat.TopChunks = Math.Clamp(Heat.TopChunks, 1, 1024);
    }
}
