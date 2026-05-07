using System.Text.RegularExpressions;

namespace MemLeakInspector.Tracking;

/// <summary>
/// Evaluates whether a type should be tracked based on allow/deny regex patterns.
/// Immutable after construction — create a new instance when filters change.
/// </summary>
internal sealed class TrackingFilter
{
    private readonly List<Regex> _allow;
    private readonly List<Regex> _deny;

    public TrackingFilter() : this([], []) { }

    public TrackingFilter(IEnumerable<string> allowPatterns, IEnumerable<string> denyPatterns)
    {
        _allow = allowPatterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new Regex(p, RegexOptions.Compiled))
            .ToList();

        _deny = denyPatterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new Regex(p, RegexOptions.Compiled))
            .ToList();
    }

    /// <summary>Returns true if the type passes the allow/deny filters.</summary>
    public bool IsAllowed(Type type)
    {
        string name = type.FullName ?? type.Name;

        // Deny takes priority
        if (_deny.Any(rx => rx.IsMatch(name))) return false;

        // If no allow list, everything (not denied) passes
        if (_allow.Count == 0) return true;

        return _allow.Any(rx => rx.IsMatch(name));
    }
}
