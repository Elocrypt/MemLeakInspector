namespace MemLeakInspector
{
    /// <summary>
    /// Represents configurable options for the MemLeakInspector mod.
    /// These values are loaded from and saved to MemLeakInspectorConfig.json.
    /// </summary>
    /// <remarks>
    /// Modify these values to tune snapshot sensitivity, memory filtering, and verbosity of tracked output.
    /// </remarks>
    public class MemLeakInspectorConfig
    {
        /// <summary>
        /// Minimum memory delta (in MB) that will trigger a memory spike warning.
        /// </summary>
        public double AlertMemorySpikeMB { get; set; } = 100.0;

        /// <summary>
        /// Minimum instance delta that will trigger an object count spike warning.
        /// </summary>
        public int AlertInstanceSpike { get; set; } = 500;

        /// <summary>
        /// Interval in seconds to run the alert watcher check.
        /// </summary>
        public int AlertCheckIntervalSec { get; set; } = 30;

        /// <summary>
        /// Minimum memory (in MB) a type must consume to appear in the memory usage report.
        /// </summary>
        public double ReportFilterMB { get; set; } = 0.0;

        /// <summary>
        /// A list of type name fragments to ignore when reporting instance spikes (e.g. "butterfly", "transient").
        /// </summary>
        public List<string> IgnoreSpikeTypeFragments { get; set; } = new()
        {
            "butterfly",
            "transient",
            "smoke",
            "sparks",
            "pollen"
        };

        /// <summary>
        /// When enabled, tracks individual instances with IDs and positions.
        /// Required for commands like /mem tp and detailed diffs.
        /// </summary>
        public bool TrackIndividualEntities { get; set; } = true;

        /// <summary>
        /// When enabled, logs individual added/removed IDs when diffing snapshots.
        /// </summary>
        public bool VerboseInstanceDiff { get; set; } = false;


        public bool AutoStartThreadWatcher { get; set; } = false;


        public int ThreadWatcherIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// If true, sleeping (ThreadState.Wait) threads are excluded from tracking/logs.
        /// </summary>
        public bool ExcludeSleepingThreads { get; set; } = true;

        /// <summary>
        /// Whether to auto-serialize thread snapshots to disk each tick.
        /// </summary>
        public bool AutoSerializeThreadSnapshots { get; set; } = false;

        /// <summary>
        /// Whether to enable thread snapshot rotation/pruning logic.
        /// </summary>
        public bool EnableThreadSnapshotRotation { get; set; } = true;

        /// <summary>
        /// Maximum number of thread snapshots to keep in memory before pruning old ones.
        /// </summary>
        public int MaxThreadSnapshotHistory { get; set; } = 180;

        /// <summary>
        /// Maximum number of lines shown in in-game diff preview before truncating to file.
        /// </summary>
        public int DiffPreviewLines { get; set; } = 15;

        public bool EnableAsyncCommands { get; set; } = true;
    }
}
