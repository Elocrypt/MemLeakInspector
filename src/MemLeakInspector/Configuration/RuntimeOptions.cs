namespace MemLeakInspector.Configuration;

/// <summary>
/// Settings for .NET System.Runtime event-counter collection.
/// </summary>
internal sealed class RuntimeOptions
{
    /// <summary>Enable runtime counter collection (alloc rate, working set, etc.).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Counter sampling interval in seconds.</summary>
    public int IntervalSec { get; set; } = 1;
}
