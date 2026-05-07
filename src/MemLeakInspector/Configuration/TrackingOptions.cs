namespace MemLeakInspector.Configuration;

/// <summary>
/// Controls which types are tracked and how individual instances are recorded.
/// </summary>
internal sealed class TrackingOptions
{
    /// <summary>Track individual instance IDs and positions (required for /mem tp and verbose diffs).</summary>
    public bool TrackIndividualEntities { get; set; } = true;

    /// <summary>Regex patterns (matched against full type names) — only matching types are tracked.</summary>
    public string[] AllowListRegex { get; set; } = [];

    /// <summary>Regex patterns — matching types are excluded from tracking.</summary>
    public string[] DenyListRegex { get; set; } = [];
}
