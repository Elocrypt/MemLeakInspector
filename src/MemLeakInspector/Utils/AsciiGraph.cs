namespace MemLeakInspector.Utils;

/// <summary>Simple ASCII bar-chart rendering for console/chat output.</summary>
internal static class AsciiGraph
{
    /// <summary>
    /// Render a proportional bar: [####          ]
    /// </summary>
    public static string Bar(int value, int max, int width = 20)
    {
        if (max <= 0) return "[" + new string(' ', width) + "]";
        int filled = Math.Clamp((int)((double)value / max * width), 0, width);
        return "[" + new string('#', filled).PadRight(width) + "]";
    }
}
