using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MemLeakInspector.Configuration;
using MemLeakInspector.Snapshots;
using MemLeakInspector.Tracking;
using MemLeakInspector.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace MemLeakInspector.Commands;

/// <summary>
/// Snapshot commands: snap, list, diff, report, export, summary, graph,
/// exportallgraphs, find, top, snapcsv, memusage.
/// </summary>
internal sealed class SnapshotCommands
{
    private readonly ICoreServerAPI _sapi;
    private readonly MemLeakInspectorConfig _config;
    private readonly SnapshotService _snap;

    public SnapshotCommands(ICoreServerAPI sapi, MemLeakInspectorConfig config, SnapshotService snap)
    {
        _sapi = sapi;
        _config = config;
        _snap = snap;
    }

    public void Register(IChatCommand root)
    {
        root.BeginSubCommand("snap")
            .WithDescription("Take a memory snapshot.")
            .WithArgs(_sapi.ChatCommands.Parsers.OptionalWord("name"))
            .HandleWith(ctx =>
            {
                string name = ctx.Parsers[0].GetValue() as string
                    ?? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                _ = _snap.TakeAndSaveAsync(name);
                return TextCommandResult.Success("[MemLeakInspector] Snapshotting in background...");
            })
        .EndSubCommand();

        root.BeginSubCommand("list")
            .WithDescription("List available snapshot files.")
            .HandleWith(_ => CmdList())
        .EndSubCommand();

        root.BeginSubCommand("diff")
            .WithDescription("Compare two snapshots by instance count change.")
            .WithArgs(
                _sapi.ChatCommands.Parsers.Word("snapshotA"),
                _sapi.ChatCommands.Parsers.Word("snapshotB"))
            .HandleWith(ctx =>
            {
                string? a = ctx.Parsers[0].GetValue()?.ToString();
                string? b = ctx.Parsers[1].GetValue()?.ToString();
                if (a is null || b is null)
                    return TextCommandResult.Error("[MemLeakInspector] Provide two snapshot names.");
                return RunMaybeAsync(() => DiffInternal(a, b));
            })
        .EndSubCommand();

        root.BeginSubCommand("report")
            .WithDescription("Show top memory types in a snapshot.")
            .WithArgs(_sapi.ChatCommands.Parsers.Word("snapshotName"))
            .HandleWith(ctx =>
            {
                string? name = ctx.Parsers[0].GetValue()?.ToString();
                if (name is null)
                    return TextCommandResult.Error("[MemLeakInspector] No snapshot name provided.");
                return RunMaybeAsync(() => ReportInternal(name));
            })
        .EndSubCommand();

        root.BeginSubCommand("export")
            .WithDescription("Export a snapshot to CSV.")
            .WithArgs(_sapi.ChatCommands.Parsers.Word("snapshotName"))
            .HandleWith(ctx =>
            {
                string? name = ctx.Parsers[0].GetValue()?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    return TextCommandResult.Error("[MemLeakInspector] Provide a snapshot name.");
                return ExportCsv(name);
            })
        .EndSubCommand();

        root.BeginSubCommand("summary")
            .WithDescription("Show top types from last N snapshots.")
            .WithArgs(_sapi.ChatCommands.Parsers.OptionalInt("count"))
            .HandleWith(ctx =>
            {
                int n = ctx.Parsers[0].GetValue() is int v ? Math.Clamp(v, 1, 100) : 10;
                return RunMaybeAsync(() => SummaryInternal(n));
            })
        .EndSubCommand();

        root.BeginSubCommand("graph")
            .WithDescription("Export time-series CSV for a type.")
            .WithArgs(
                _sapi.ChatCommands.Parsers.Word("typeName"),
                _sapi.ChatCommands.Parsers.OptionalInt("count"))
            .HandleWith(ctx =>
            {
                string? type = ctx.Parsers[0].GetValue()?.ToString();
                int limit = ctx.Parsers[1].GetValue() is int v ? Math.Clamp(v, 1, 1000) : 20;
                if (string.IsNullOrWhiteSpace(type))
                    return TextCommandResult.Error("[MemLeakInspector] Provide a type name.");
                return RunMaybeAsync(() => GraphInternal(type, limit));
            })
        .EndSubCommand();

        root.BeginSubCommand("top")
            .WithDescription("Top growth since last snapshot.")
            .WithArgs(_sapi.ChatCommands.Parsers.OptionalInt("n"))
            .HandleWith(ctx =>
            {
                int n = ctx.Parsers[0].GetValue() is int v ? v : 20;
                return CmdTop(n);
            })
        .EndSubCommand();

        root.BeginSubCommand("find")
            .WithDescription("Regex filter against latest snapshot's type counts.")
            .WithArgs(_sapi.ChatCommands.Parsers.WordRange("regex"))
            .HandleWith(ctx =>
            {
                string pattern = ctx.Parsers[0].GetValue()!.ToString()!;
                return CmdFind(pattern);
            })
        .EndSubCommand();

        root.BeginSubCommand("memusage")
            .WithDescription("Show estimated memory usage by type from a snapshot.")
            .WithArgs(_sapi.ChatCommands.Parsers.Word("snapshotName"))
            .HandleWith(ctx =>
            {
                string? name = ctx.Parsers[0].GetValue()?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    return TextCommandResult.Error("[MemLeakInspector] Provide a snapshot name.");
                return MemUsageInternal(name);
            })
        .EndSubCommand();

        root.BeginSubCommand("snapcsv")
            .WithDescription("Export snapshot instances to CSV with TRUE and HUD coords.")
            .WithArgs(_sapi.ChatCommands.Parsers.Word("snapshotName"))
            .HandleWith(ctx =>
            {
                string name = ctx.Parsers[0].GetValue()!.ToString()!;
                return CmdSnapCsv(name);
            })
        .EndSubCommand();
    }

    // ------------------------------------------------------------------
    //  Implementations
    // ------------------------------------------------------------------

    private TextCommandResult CmdList()
    {
        var rows = _snap.ListAll();
        if (rows.Count == 0)
            return TextCommandResult.Success("[MemLeakInspector] No snapshots found.");

        var sb = new StringBuilder();
        sb.AppendLine("[MemLeakInspector] Snapshots (newest first):");
        foreach (var (t, n) in rows.Take(200))
            sb.AppendLine($"  {t:yyyy-MM-dd HH:mm:ss}  {n}");
        return TextCommandResult.Success(sb.ToString());
    }

    private TextCommandResult DiffInternal(string nameA, string nameB)
    {
        string? pathA = _snap.FindSnapshot(nameA);
        string? pathB = _snap.FindSnapshot(nameB);
        if (pathA is null || pathB is null)
            return TextCommandResult.Error("[MemLeakInspector] Snapshot file(s) not found.");

        var sa = _snap.Load(pathA);
        var sb = _snap.Load(pathB);
        if (sa is null || sb is null)
            return TextCommandResult.Error("[MemLeakInspector] Failed to load snapshot(s).");

        _sapi.Logger.Notification($"[MemLeakInspector] Diff: {nameA} → {nameB}");
        foreach (var kv in SnapshotDiff.DiffTypes(sa, sb).OrderByDescending(kv => kv.Value).Take(100))
            _sapi.Logger.Notification($"[Δ] {kv.Key}: {(kv.Value > 0 ? "+" : "")}{kv.Value}");

        return TextCommandResult.Success("[MemLeakInspector] Diff printed.");
    }

    private TextCommandResult ReportInternal(string name)
    {
        string? path = _snap.FindSnapshot(name);
        if (path is null)
            return TextCommandResult.Error($"[MemLeakInspector] Snapshot '{name}' not found.");

        var snap = _snap.Load(path);
        if (snap is null)
            return TextCommandResult.Error($"[MemLeakInspector] Failed to load '{name}'.");

        var top = snap.TypeCounts
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .ToList();

        _sapi.Logger.Notification($"[MemLeakInspector] Report: '{name}'");
        _sapi.Logger.Notification($"  Heap: {snap.TotalManagedMemoryBytes / 1024 / 1024} MB");
        foreach (var (type, count) in top)
            _sapi.Logger.Notification($"  {count,6} × {type}");

        return TextCommandResult.Success("[MemLeakInspector] Report complete.");
    }

    private TextCommandResult ExportCsv(string name)
    {
        string? path = _snap.FindSnapshot(name);
        if (path is null)
            return TextCommandResult.Error($"[MemLeakInspector] Snapshot '{name}' not found.");

        var snap = _snap.Load(path);
        if (snap is null)
            return TextCommandResult.Error("[MemLeakInspector] Failed to load snapshot.");

        string csvPath = Path.Combine(_snap.SnapshotDir, $"{name}.csv");
        using var writer = new StreamWriter(csvPath);
        writer.WriteLine("TypeName,InstanceCount");
        foreach (var kv in snap.TypeCounts.OrderByDescending(kv => kv.Value))
            writer.WriteLine($"\"{kv.Key}\",{kv.Value}");

        return TextCommandResult.Success($"[MemLeakInspector] Exported to: {Path.GetFileName(csvPath)}");
    }

    private TextCommandResult SummaryInternal(int count)
    {
        var files = Directory.GetFiles(_snap.SnapshotDir, "*.json*")
            .OrderByDescending(File.GetLastWriteTime)
            .Take(count)
            .ToList();

        if (files.Count == 0)
            return TextCommandResult.Error("[MemLeakInspector] No snapshots available.");

        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        var memory = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var snap = SnapshotStore.Load(file);
            if (snap?.TypeCounts is null) continue;

            foreach (var kv in snap.TypeCounts)
                totals[kv.Key] = totals.GetValueOrDefault(kv.Key) + kv.Value;

            if (snap.EstimatedMemoryBytesPerType is not null)
                foreach (var kv in snap.EstimatedMemoryBytesPerType)
                    memory[kv.Key] = memory.GetValueOrDefault(kv.Key) + kv.Value;
        }

        // Average
        foreach (var key in totals.Keys.ToList())
            totals[key] /= files.Count;

        if (totals.Count == 0)
            return TextCommandResult.Success("[MemLeakInspector] No tracked types found.");

        _sapi.Logger.Notification($"[MemLeakInspector] Summary across {files.Count} snapshots:");
        int max = totals.Max(kv => kv.Value);

        foreach (var entry in totals.OrderByDescending(kv => kv.Value).Take(10))
        {
            string bar = AsciiGraph.Bar(entry.Value, max);
            memory.TryGetValue(entry.Key, out long mem);
            double memMB = mem / (1024.0 * 1024.0);
            _sapi.Logger.Notification($"{bar}  {entry.Value,5} x {entry.Key,-60} ≈ {memMB,6:F1} MB");
        }

        return TextCommandResult.Success("[MemLeakInspector] Summary complete.");
    }

    private TextCommandResult GraphInternal(string typeName, int limit)
    {
        var files = Directory.GetFiles(_snap.SnapshotDir, "*.json*")
            .OrderByDescending(File.GetLastWriteTime)
            .Take(limit)
            .OrderBy(File.GetLastWriteTime)
            .ToList();

        if (files.Count == 0)
            return TextCommandResult.Error("[MemLeakInspector] No snapshots found.");

        string exportPath = Path.Combine(_snap.SnapshotDir,
            $"graph_{typeName.Replace(":", "_")}.csv");

        var data = new List<(DateTime time, int count)>();

        using (var writer = new StreamWriter(exportPath))
        {
            writer.WriteLine("Timestamp,InstanceCount");
            foreach (var file in files)
            {
                var snap = SnapshotStore.Load(file);
                if (snap is null || snap.Timestamp == default) continue;

                int count = snap.TypeCounts
                    .Where(kv => kv.Key.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                    .Sum(kv => kv.Value);

                data.Add((snap.Timestamp, count));
                writer.WriteLine($"{snap.Timestamp:yyyy-MM-dd HH:mm:ss},{count}");
            }
        }

        if (data.Count > 0)
        {
            _sapi.Logger.Notification($"[MemLeakInspector] History for '{typeName}':");
            int maxCount = data.Max(dp => dp.count);
            int? prev = null;
            foreach (var (time, count) in data)
            {
                int delta = prev.HasValue ? count - prev.Value : 0;
                string ds = prev.HasValue ? $" ({(delta >= 0 ? "+" : "")}{delta})" : "";
                string bar = AsciiGraph.Bar(count, maxCount);
                _sapi.Logger.Notification($"{time:HH:mm:ss} {bar} {count}{ds}");
                prev = count;
            }
        }

        return TextCommandResult.Success($"[MemLeakInspector] Graph exported: {Path.GetFileName(exportPath)}");
    }

    private TextCommandResult CmdTop(int n)
    {
        if (_snap.LastSnapshot is null)
            return TextCommandResult.Success("[MemLeakInspector] No prior snapshot.");

        var cur = _snap.Build("(top)");
        foreach (var kv in SnapshotDiff.DiffTypes(_snap.LastSnapshot, cur)
            .OrderByDescending(kv => kv.Value).Take(n))
        {
            _sapi.Logger.Notification($"[Top] {kv.Key}: +{kv.Value}");
        }
        _snap.LastSnapshot = cur;
        return TextCommandResult.Success("[MemLeakInspector] Top growth listed.");
    }

    private TextCommandResult CmdFind(string pattern)
    {
        if (_snap.LastSnapshot is null)
            return TextCommandResult.Success("[MemLeakInspector] No snapshot yet.");

        var rx = new Regex(pattern, RegexOptions.IgnoreCase);
        foreach (var kv in _snap.LastSnapshot.TypeCounts
            .Where(k => rx.IsMatch(k.Key))
            .OrderByDescending(kv => kv.Value))
        {
            _sapi.Logger.Notification($"[Find] {kv.Key}: {kv.Value}");
        }
        return TextCommandResult.Success("[MemLeakInspector] Find complete.");
    }

    private TextCommandResult MemUsageInternal(string name)
    {
        string? path = _snap.FindSnapshot(name);
        if (path is null)
            return TextCommandResult.Error($"[MemLeakInspector] Snapshot '{name}' not found.");

        var snap = _snap.Load(path);
        if (snap?.EstimatedMemoryBytesPerType is null || snap.EstimatedMemoryBytesPerType.Count == 0)
            return TextCommandResult.Error("[MemLeakInspector] Snapshot missing memory data.");

        // Seed the size cache from the snapshot
        if (snap.EstimatedBytesPerType is not null)
            SizeEstimator.Seed(snap.EstimatedBytesPerType);

        _sapi.Logger.Notification($"[MemLeakInspector] Memory usage in '{name}':");
        foreach (var entry in snap.EstimatedMemoryBytesPerType.OrderByDescending(kv => kv.Value))
        {
            double mb = entry.Value / (1024.0 * 1024.0);
            if (mb < _config.ReportFilterMB) continue;
            _sapi.Logger.Notification($"    {entry.Key} = {mb:F1} MB");
        }

        return TextCommandResult.Success("[MemLeakInspector] Memory usage report complete.");
    }

    private TextCommandResult CmdSnapCsv(string name)
    {
        string? path = _snap.FindSnapshot(name);
        if (path is null)
            return TextCommandResult.Error("[MemLeakInspector] Snapshot not found: " + name);

        var snap = _snap.Load(path);
        if (snap is null)
            return TextCommandResult.Error("[MemLeakInspector] Failed to load: " + name);

        string outPath = Path.Combine(_snap.SnapshotDir, $"instances-{name}.csv");
        SnapshotCsv.WriteInstancesCsv(snap, outPath);
        return TextCommandResult.Success("[MemLeakInspector] Instance CSV exported.");
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private TextCommandResult RunMaybeAsync(Func<TextCommandResult> work)
    {
        if (!_config.EnableAsyncCommands)
            return work();

        _ = Task.Run(() =>
        {
            var result = work();
            _sapi.Logger.Notification(result.StatusMessage);
        });
        return TextCommandResult.Success("[MemLeakInspector] Running in background...");
    }
}
