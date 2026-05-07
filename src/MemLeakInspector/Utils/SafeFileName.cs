using System.Text.RegularExpressions;

namespace MemLeakInspector.Utils;

/// <summary>
/// Sanitizes strings for safe use as file names across platforms.
/// </summary>
internal static partial class SafeFileName
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>Replace invalid filename characters with underscores.</summary>
    public static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unnamed";

        string result = input;
        foreach (char c in InvalidChars)
            result = result.Replace(c, '_');

        // Collapse runs of underscores
        result = CollapseUnderscores().Replace(result, "_");

        // Trim to reasonable length
        if (result.Length > 100)
            result = result[..100];

        return result.Trim('_', ' ', '.');
    }

    [GeneratedRegex("_{2,}")]
    private static partial Regex CollapseUnderscores();
}
