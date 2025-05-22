namespace MemLeakInspector
{
    public class MemSnapshot
    {
        public DateTime Timestamp { get; set; }
        public long TotalManagedMemoryBytes { get; set; }
        public Dictionary<string, int> ObjectCountsByType { get; set; } = new();
        public Dictionary<string, int>? EstimatedBytesPerType { get; set; }
        public Dictionary<string, long> EstimatedMemoryBytesPerType { get; set; } = new();

    }
}
