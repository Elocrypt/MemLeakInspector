namespace MemLeakInspector.Configuration;

/// <summary>
/// Settings for background thread-state monitoring.
/// </summary>
internal sealed class ThreadOptions
{
    /// <summary>Start the thread watcher automatically on server load.</summary>
    public bool AutoStart { get; set; }

    /// <summary>Polling interval in seconds.</summary>
    public int IntervalSec { get; set; } = 30;

    /// <summary>Exclude sleeping/waiting threads from logs.</summary>
    public bool ExcludeSleepingThreads { get; set; } = true;

    /// <summary>Auto-serialize each thread snapshot to disk.</summary>
    public bool AutoSerialize { get; set; }

    /// <summary>Enable oldest-first pruning of in-memory thread history.</summary>
    public bool EnableRotation { get; set; } = true;

    /// <summary>Maximum thread snapshots kept in memory before pruning.</summary>
    public int MaxHistory { get; set; } = 180;
}
