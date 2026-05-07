using System.Text;
using System.Text.Json;
using MemLeakInspector.Configuration;
using Vintagestory.API.Server;

namespace MemLeakInspector.Diagnostics;

/// <summary>
/// Polls OS-level thread state on an interval and maintains a rolling history.
/// Provides export to CSV, JSON, and ASCII graph.
/// </summary>
internal sealed class ThreadMonitor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ThreadOptions _opts;
    private readonly List<ThreadSnapshotEntry> _history = [];
    private bool _running;
    private string _logPath = "";
    private string _snapshotDir = "";
    private ICoreServerAPI? _sapi;

    public IReadOnlyList<ThreadSnapshotEntry> History => _history;
    public bool IsRunning => _running;

    public ThreadMonitor(ThreadOptions opts)
    {
        _opts = opts;
    }

    public void Start(ICoreServerAPI sapi, string snapshotDir)
    {
        if (_running) return;
        _sapi = sapi;
        _snapshotDir = snapshotDir;
        _running = true;
        _logPath = Path.Combine(snapshotDir, $"threadlog-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        _history.Clear();
        sapi.World.RegisterCallback(Tick, 1000);
    }

    public void Stop() => _running = false;

    /// <summary>Take a single thread snapshot (for /mem threads).</summary>
    public ThreadSnapshotEntry TakeSnapshot()
    {
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        var entry = new ThreadSnapshotEntry { Timestamp = DateTime.Now };

        foreach (System.Diagnostics.ProcessThread t in proc.Threads)
        {
            try
            {
                if (t.Id <= 0) continue;
                if (_opts.ExcludeSleepingThreads &&
                    t.ThreadState == System.Diagnostics.ThreadState.Wait) continue;

                entry.Threads.Add(new ThreadInfo
                {
                    Id = t.Id,
                    State = t.ThreadState.ToString(),
                    WaitReason = t.ThreadState == System.Diagnostics.ThreadState.Wait
                        ? t.WaitReason.ToString() : null,
                    CpuTimeMs = t.TotalProcessorTime.TotalMilliseconds > 0
                        ? (long)t.TotalProcessorTime.TotalMilliseconds : 0
                });
            }
            catch { /* some threads not accessible */ }
        }
        return entry;
    }

    /// <summary>Export thread history to CSV.</summary>
    public string ExportCsv(string snapshotDir)
    {
        string dir = Path.Combine(snapshotDir, "exports");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"threadwatch-{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

        using var writer = new StreamWriter(path);
        writer.WriteLine("Timestamp,ThreadId,State,WaitReason,TotalCpuMs");
        foreach (var entry in _history)
            foreach (var t in entry.Threads)
                writer.WriteLine($"{entry.Timestamp:O},{t.Id},{t.State},{t.WaitReason},{t.CpuTimeMs}");

        return path;
    }

    /// <summary>Export thread history to JSON.</summary>
    public string ExportJson(string snapshotDir)
    {
        string dir = Path.Combine(snapshotDir, "threaddump");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"threadhistory-{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

        File.WriteAllText(path, JsonSerializer.Serialize(_history, JsonOpts));
        return path;
    }

    /// <summary>Generate ASCII thread-count graph from history.</summary>
    public string GenerateGraph()
    {
        if (_history.Count < 2) return "(not enough data)";

        int max = _history.Max(e => e.Threads.Count);
        var sb = new StringBuilder();
        foreach (var entry in _history)
        {
            int count = entry.Threads.Count;
            int barLen = max > 0 ? (int)(count / (float)max * 40) : 0;
            string bar = new('#', barLen);
            sb.AppendLine($"[{entry.Timestamp:HH:mm:ss}] {count,3} | {bar}");
        }
        return sb.ToString();
    }

    /// <summary>Write graph to file and return path.</summary>
    public string SaveGraph(string snapshotDir)
    {
        string path = Path.Combine(snapshotDir, $"threadgraph-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, GenerateGraph());
        return path;
    }

    // ------------------------------------------------------------------

    private void Tick(float dt)
    {
        if (!_running) return;

        try
        {
            var entry = TakeSnapshot();
            _history.Add(entry);

            if (_opts.EnableRotation && _opts.MaxHistory > 0)
                while (_history.Count > _opts.MaxHistory)
                    _history.RemoveAt(0);

            if (_opts.AutoSerialize)
                AutoSerializeEntry(entry);

            AppendLog(entry);
        }
        catch (Exception ex)
        {
            _sapi?.Logger.Warning($"[MemLeakInspector] ThreadMonitor error: {ex.Message}");
        }

        _sapi?.World.RegisterCallback(Tick, _opts.IntervalSec * 1000);
    }

    private void AutoSerializeEntry(ThreadSnapshotEntry entry)
    {
        try
        {
            string dir = Path.Combine(_snapshotDir, "threadauto");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"thread-{entry.Timestamp:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(entry, JsonOpts));
        }
        catch (Exception ex)
        {
            _sapi?.Logger.Warning($"[MemLeakInspector] Thread auto-serialize failed: {ex.Message}");
        }
    }

    private void AppendLog(ThreadSnapshotEntry entry)
    {
        try
        {
            var lines = new List<string> { $"{entry.Timestamp:HH:mm:ss} Threads: {entry.Threads.Count}" };
            foreach (var t in entry.Threads)
            {
                string line = $" - ID {t.Id} | State: {t.State}";
                if (t.WaitReason is not null) line += $" | Wait: {t.WaitReason}";
                line += $" | CPU: {t.CpuTimeMs}ms";
                lines.Add(line);
            }
            File.AppendAllLines(_logPath, lines);
        }
        catch { /* best-effort */ }
    }
}

// ------------------------------------------------------------------
//  Data models
// ------------------------------------------------------------------

internal sealed class ThreadSnapshotEntry
{
    public DateTime Timestamp { get; set; }
    public List<ThreadInfo> Threads { get; set; } = [];
}

internal sealed class ThreadInfo
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string? WaitReason { get; set; }
    public long CpuTimeMs { get; set; }
}
