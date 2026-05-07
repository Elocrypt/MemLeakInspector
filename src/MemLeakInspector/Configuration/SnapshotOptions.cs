namespace MemLeakInspector.Configuration;

/// <summary>
/// Settings governing snapshot capture, storage, and export behavior.
/// </summary>
internal sealed class SnapshotOptions
{
    /// <summary>Force a full GC.Collect before each snapshot for more accurate counts.</summary>
    public bool ForceFullGcBeforeSnapshot { get; set; }

    /// <summary>Maximum snapshots retained on disk before oldest are pruned.</summary>
    public int MaxSnapshotsOnDisk { get; set; } = 200;

    /// <summary>GZip-compress snapshot JSON files.</summary>
    public bool CompressSnapshots { get; set; } = true;

    /// <summary>Maximum diff preview lines shown in chat before truncating to file.</summary>
    public int DiffPreviewLines { get; set; } = 15;

    /// <summary>Include full tracked instance ID lists when diffing snapshots.</summary>
    public bool VerboseInstanceDiff { get; set; }

    /// <summary>Use separate subfolders for autosnap, heatmap, threads.</summary>
    public bool UseSubfolders { get; set; } = true;
}
