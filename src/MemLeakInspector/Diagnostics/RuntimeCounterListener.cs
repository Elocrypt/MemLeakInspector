using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;

namespace MemLeakInspector.Diagnostics;

/// <summary>
/// Listens to .NET System.Runtime event counters (alloc rate, working set, GC, etc.)
/// and exposes current + 60-second-average snapshots.
/// </summary>
internal sealed class RuntimeCounterListener : EventListener
{
    public record Sample(DateTime T, double Value);

    private readonly object _gate = new();
    private readonly Dictionary<string, List<Sample>> _series = [];

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "System.Runtime")
        {
            EnableEvents(eventSource, EventLevel.Informational, EventKeywords.None,
                new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName != "EventCounters" || e.Payload is null || e.Payload.Count == 0) return;
        var payload = (IDictionary<string, object?>)e.Payload[0]!;
        string name = (string)payload["Name"]!;
        double val = Convert.ToDouble(payload["Mean"] ?? payload["Increment"] ?? 0d);

        lock (_gate)
        {
            if (!_series.TryGetValue(name, out var list))
                _series[name] = list = [];
            list.Add(new Sample(DateTime.UtcNow, val));
            if (list.Count > 300) list.RemoveRange(0, list.Count - 300);
        }
    }

    /// <summary>Current values and 60-second averages for all counters.</summary>
    public Dictionary<string, (double cur, double avg60s)> Snapshot()
    {
        lock (_gate)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-60);
            return _series.ToDictionary(
                kv => kv.Key,
                kv =>
                {
                    var last = kv.Value.LastOrDefault();
                    var seg = kv.Value.Where(s => s.T >= cutoff).Select(s => s.Value);
                    return (last?.Value ?? 0, seg.Any() ? seg.Average() : 0);
                });
        }
    }

    /// <summary>Export all counter history as a wide CSV table.</summary>
    public string ToWideCsv()
    {
        Dictionary<string, List<Sample>> copy;
        lock (_gate) copy = _series.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());

        var times = copy.Values.SelectMany(v => v.Select(s => s.T))
            .Select(t => new DateTime(t.Ticks - (t.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc))
            .Distinct().OrderBy(t => t).ToList();

        var names = copy.Keys.OrderBy(n => n).ToList();
        var sb = new StringBuilder();
        sb.Append("timestamp");
        foreach (var n in names) sb.Append(',').Append(n);
        sb.AppendLine();

        var idx = names.ToDictionary(n => n, _ => 0);
        foreach (var t in times)
        {
            sb.Append(t.ToString("O"));
            foreach (var n in names)
            {
                var list = copy[n];
                int i = idx[n];
                while (i + 1 < list.Count && list[i + 1].T <= t) i++;
                idx[n] = i;
                var val = (i < list.Count && list[i].T <= t) ? list[i].Value : double.NaN;
                sb.Append(',').Append(double.IsNaN(val) ? "" : val.ToString(CultureInfo.InvariantCulture));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
