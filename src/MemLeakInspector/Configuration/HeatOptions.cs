namespace MemLeakInspector.Configuration;

/// <summary>
/// Settings for the in-world heat-highlight overlay.
/// </summary>
internal sealed class HeatOptions
{
    /// <summary>Enable the server-side heat overlay system.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum seconds between highlight packets to avoid spam.</summary>
    public int CooldownSec { get; set; } = 15;

    /// <summary>Maximum distance (blocks) from a player to include a chunk in highlights.</summary>
    public int MaxDistance { get; set; } = 256;

    /// <summary>Maximum number of top-growth chunks to highlight per cycle.</summary>
    public int TopChunks { get; set; } = 128;
}
