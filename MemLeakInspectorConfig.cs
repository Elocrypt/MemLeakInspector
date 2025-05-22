namespace MemLeakInspector
{
    public class MemLeakInspectorConfig
    {
        public double AlertMemorySpikeMB { get; set; } = 100.0;
        public int AlertInstanceSpike { get; set; } = 500;
        public int AlertCheckIntervalSec { get; set; } = 30;
        public double ReportFilterMB { get; set; } = 0.0;
        /// <summary>
        /// A list of type name *fragments* to ignore when reporting instance spikes (e.g. "butterfly", "transient").
        /// </summary>
        public List<string> IgnoreSpikeTypeFragments { get; set; } = new()
        {
            "butterfly",
            "transient",
            "smoke",
            "sparks",
            "pollen"
        };
    }
}
