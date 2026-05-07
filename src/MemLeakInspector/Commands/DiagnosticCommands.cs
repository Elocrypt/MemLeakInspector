using MemLeakInspector.Configuration;
using MemLeakInspector.Diagnostics;
using MemLeakInspector.Snapshots;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace MemLeakInspector.Commands;

/// <summary>
/// Diagnostic commands: threads, threadwatch, threadwatchstop, threadexport,
/// threaddump, runtime, runtimecsv, alertwatch, alertstop.
/// </summary>
internal sealed class DiagnosticCommands
{
    private readonly ICoreServerAPI _sapi;
    private readonly MemLeakInspectorConfig _config;
    private readonly ThreadMonitor _threads;
    private readonly AlertWatcher _alerts;
    private readonly RuntimeCounterListener? _counters;
    private readonly SnapshotService _snap;

    public DiagnosticCommands(ICoreServerAPI sapi, MemLeakInspectorConfig config,
        ThreadMonitor threadMonitor, AlertWatcher alertWatcher,
        RuntimeCounterListener? counters, SnapshotService snapService)
    {
        _sapi = sapi;
        _config = config;
        _threads = threadMonitor;
        _alerts = alertWatcher;
        _counters = counters;
        _snap = snapService;
    }

    public void Register(IChatCommand root)
    {
        root.BeginSubCommand("alertwatch")
            .WithDescription("Start real-time memory/instance spike detection.")
            .HandleWith(_ => TextCommandResult.Success(_alerts.Start()))
        .EndSubCommand();

        root.BeginSubCommand("alertstop")
            .WithDescription("Stop spike detection.")
            .HandleWith(_ => TextCommandResult.Success(_alerts.Stop()))
        .EndSubCommand();

        root.BeginSubCommand("threads")
            .WithDescription("Show current server process thread stats.")
            .HandleWith(_ => CmdListThreads())
        .EndSubCommand();

        root.BeginSubCommand("threadwatch")
            .WithDescription("Start background thread state logging.")
            .WithArgs(_sapi.ChatCommands.Parsers.OptionalInt("intervalSec"))
            .HandleWith(ctx =>
            {
                if (_threads.IsRunning)
                    return TextCommandResult.Success("[MemLeakInspector] Thread watcher already running.");
                _threads.Start(_sapi, _snap.SnapshotDir);
                return TextCommandResult.Success("[MemLeakInspector] Thread watcher started.");
            })
        .EndSubCommand();

        root.BeginSubCommand("threadwatchstop")
            .WithDescription("Stop background thread state logging.")
            .HandleWith(_ =>
            {
                if (!_threads.IsRunning)
                    return TextCommandResult.Success("[MemLeakInspector] Thread watcher not running.");

                // Save graph before stopping
                if (_threads.History.Count > 1)
                {
                    string graphPath = _threads.SaveGraph(_snap.SnapshotDir);
                    _sapi.Logger.Notification($"[MemLeakInspector] Thread graph saved: {Path.GetFileName(graphPath)}");
                }

                _threads.Stop();
                return TextCommandResult.Success("[MemLeakInspector] Thread watcher stopped.");
            })
        .EndSubCommand();

        root.BeginSubCommand("threadexport")
            .WithDescription("Export thread watcher history to CSV.")
            .HandleWith(_ =>
            {
                if (_threads.History.Count == 0)
                    return TextCommandResult.Error("[MemLeakInspector] No threadwatch data.");
                try
                {
                    string path = _threads.ExportCsv(_snap.SnapshotDir);
                    _sapi.Logger.Notification($"[MemLeakInspector] Thread CSV: {Path.GetFileName(path)}");
                    return TextCommandResult.Success("[MemLeakInspector] Threadwatch exported to CSV.");
                }
                catch (Exception ex)
                {
                    return TextCommandResult.Error($"[MemLeakInspector] Export failed: {ex.Message}");
                }
            })
        .EndSubCommand();

        root.BeginSubCommand("threaddump")
            .WithDescription("Export thread watcher history to JSON.")
            .HandleWith(_ =>
            {
                if (_threads.History.Count == 0)
                    return TextCommandResult.Error("[MemLeakInspector] No thread history.");
                try
                {
                    string path = _threads.ExportJson(_snap.SnapshotDir);
                    _sapi.Logger.Notification($"[MemLeakInspector] Thread JSON: {Path.GetFileName(path)}");
                    return TextCommandResult.Success("[MemLeakInspector] Thread history exported.");
                }
                catch (Exception ex)
                {
                    return TextCommandResult.Error($"[MemLeakInspector] Export failed: {ex.Message}");
                }
            })
        .EndSubCommand();

        root.BeginSubCommand("runtime")
            .WithDescription("Show .NET runtime counters (now + 60s avg).")
            .HandleWith(_ =>
            {
                var snap = _counters?.Snapshot();
                if (snap is null || snap.Count == 0)
                    return TextCommandResult.Success("[MemLeakInspector] No counter data yet.");
                foreach (var (name, (cur, avg)) in snap.OrderBy(k => k.Key))
                    _sapi.Logger.Notification($"[ctr] {name}: now={cur:F1} avg60s={avg:F1}");
                return TextCommandResult.Success("[MemLeakInspector] Runtime counters printed.");
            })
        .EndSubCommand();

        root.BeginSubCommand("runtimecsv")
            .WithDescription("Export runtime counters to a wide CSV.")
            .WithArgs(_sapi.ChatCommands.Parsers.OptionalWord("name"))
            .HandleWith(ctx =>
            {
                if (_counters is null)
                    return TextCommandResult.Success("[MemLeakInspector] Counters disabled.");

                string name = ctx.Parsers[0].GetValue() as string
                    ?? "runtime-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(_snap.SnapshotDir, name + ".csv");
                File.WriteAllText(path, _counters.ToWideCsv());
                return TextCommandResult.Success($"[MemLeakInspector] Wrote {Path.GetFileName(path)}");
            })
        .EndSubCommand();
    }

    private TextCommandResult CmdListThreads()
    {
        var entry = _threads.TakeSnapshot();
        string path = Path.Combine(_snap.SnapshotDir,
            $"threads-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var lines = new List<string>
        {
            $"[MemLeakInspector] Thread snapshot at {entry.Timestamp:yyyy-MM-dd HH:mm:ss}",
            $"Total threads: {entry.Threads.Count}"
        };
        foreach (var t in entry.Threads)
        {
            string line = $"#{t.Id} State={t.State}";
            if (t.WaitReason is not null) line += $" Wait={t.WaitReason}";
            line += $" CPU={t.CpuTimeMs}ms";
            lines.Add(line);
        }

        File.WriteAllLines(path, lines);
        return TextCommandResult.Success(
            $"[MemLeakInspector] Exported {entry.Threads.Count} threads: {Path.GetFileName(path)}");
    }
}
