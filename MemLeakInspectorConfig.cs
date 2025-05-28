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
        public bool TrackIndividualEntities { get; set; } = false;

        /// <summary>
        /// When enabled, logs individual added/removed IDs when diffing snapshots.
        /// </summary>
        public bool VerboseInstanceDiff { get; set; } = false;

    }
}
