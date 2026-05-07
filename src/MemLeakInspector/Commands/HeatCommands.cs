using MemLeakInspector.Configuration;
using MemLeakInspector.Rendering;
using MemLeakInspector.Snapshots;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace MemLeakInspector.Commands;

/// <summary>
/// Heat overlay commands: showheat, heatmap, heatmapexport, heatmapcsv.
/// </summary>
internal sealed class HeatCommands
{
    private readonly ICoreServerAPI _sapi;
    private readonly MemLeakInspectorConfig _config;
    private readonly SnapshotService _snap;
    private readonly HeatHighlighter? _highlighter;

    public HeatCommands(ICoreServerAPI sapi, MemLeakInspectorConfig config,
        SnapshotService snap, HeatHighlighter? highlighter)
    {
        _sapi = sapi;
        _config = config;
        _snap = snap;
        _highlighter = highlighter;
    }

    public void Register(IChatCommand root)
    {
        root.BeginSubCommand("showheat")
            .WithDescription("Highlight leaking instances in the world.")
            .HandleWith(_ => CmdShowHeat())
        .EndSubCommand();

        root.BeginSubCommand("heatmap")
            .WithDescription("Compare two snapshots for growth/shrinkage.")
            .WithArgs(
                _sapi.ChatCommands.Parsers.Word("snapshotOld"),
                _sapi.ChatCommands.Parsers.Word("snapshotNew"))
            .HandleWith(ctx =>
            {
                string? a = ctx.Parsers[0].GetValue()?.ToString();
                string? b = ctx.Parsers[1].GetValue()?.ToString();
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                    return TextCommandResult.Error("[MemLeakInspector] Provide two snapshot names.");
                return RunMaybeAsync(() => HeatmapInternal(a, b));
            })
        .EndSubCommand();

        root.BeginSubCommand("heatmapexport")
            .WithDescription("Export snapshot delta to CSV.")
            .WithArgs(
                _sapi.ChatCommands.Parsers.Word("oldSnapshot"),
                _sapi.ChatCommands.Parsers.Word("newSnapshot"))
            .HandleWith(ctx =>
            {
                string? a = ctx.Parsers[0].GetValue()?.ToString();
                string? b = ctx.Parsers[1].GetValue()?.ToString();
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                    return TextCommandResult.Error("[MemLeakInspector] Provide two snapshot names.");
                return HeatmapExportCsv(a, b);
            })
        .EndSubCommand();

        root.BeginSubCommand("heatmapcsv")
            .WithDescription("Export chunk growth to CSV with TRUE and HUD coords.")
            .WithArgs(
                _sapi.ChatCommands.Parsers.Word("older"),
                _sapi.ChatCommands.Parsers.Word("newer"))
            .HandleWith(ctx =>
            {
                string a = ctx.Parsers[0].GetValue()!.ToString()!;
                string b = ctx.Parsers[1].GetValue()!.ToString()!;
                return ChunkHeatmapCsv(a, b);
            })
        .EndSubCommand();
    }

    // ------------------------------------------------------------------

    private TextCommandResult CmdShowHeat()
    {
        if (!_config.Heat.Enabled)
            return TextCommandResult.Success("[MemLeakInspector] Heat overlay disabled in config.");
        if (_highlighter is null)
            return TextCommandResult.Success("[MemLeakInspector] Highlighter not initialized.");
        if (_snap.LastSnapshot is null)
            return TextCommandResult.Success("[MemLeakInspector] No prior snapshot — take one first.");

        var prev = _snap.LastSnapshot;
        var cur = _snap.Build("(heat)");
        _highlighter.MaybeSend(prev, cur);
        _snap.LastSnapshot = cur;

        return TextCommandResult.Success("[MemLeakInspector] Heat overlay sent (if growth detected).");
    }

    private TextCommandResult HeatmapInternal(string oldName, string newName)
    {
        var sa = LoadSnap(oldName);
        var sb = LoadSnap(newName);
        if (sa is null || sb is null)
            return TextCommandResult.Error("[MemLeakInspector] Failed to load snapshot(s).");

        var deltas = new Dictionary<string, int>();
        foreach (var key in sa.TypeCounts.Keys.Union(sb.TypeCounts.Keys))
        {
            int v1 = sa.TypeCounts.GetValueOrDefault(key);
            int v2 = sb.TypeCounts.GetValueOrDefault(key);
            int d = v2 - v1;
            if (d != 0) deltas[key] = d;
        }

        if (deltas.Count == 0)
            return TextCommandResult.Success("[MemLeakInspector] No changes detected.");

        _sapi.Logger.Notification($"[MemLeakInspector] Heatmap: {oldName} → {newName}");
        foreach (var entry in deltas.OrderByDescending(kv => Math.Abs(kv.Value)).Take(10))
        {
            string sign = entry.Value > 0 ? "+" : "";
            _sapi.Logger.Notification($"    {sign}{entry.Value,5}  {entry.Key}");
        }

        return TextCommandResult.Success("[MemLeakInspector] Heatmap generated.");
    }

    private TextCommandResult HeatmapExportCsv(string oldName, string newName)
    {
        var sa = LoadSnap(oldName);
        var sb = LoadSnap(newName);
        if (sa is null || sb is null)
            return TextCommandResult.Error("[MemLeakInspector] Failed to load snapshot(s).");

        string dir = Path.Combine(_snap.SnapshotDir, "exports");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"heatmap_{oldName}_to_{newName}.csv");

        var deltas = new List<(string type, int delta)>();
        foreach (var key in sa.TypeCounts.Keys.Union(sb.TypeCounts.Keys))
        {
            int d = sb.TypeCounts.GetValueOrDefault(key) - sa.TypeCounts.GetValueOrDefault(key);
            if (d != 0) deltas.Add((key, d));
        }

        using var writer = new StreamWriter(path);
        writer.WriteLine("TypeName,Delta");
        foreach (var (type, delta) in deltas.OrderByDescending(e => Math.Abs(e.delta)))
            writer.WriteLine($"\"{type}\",{delta}");

        return TextCommandResult.Success($"[MemLeakInspector] Heatmap exported: {Path.GetFileName(path)}");
    }

    private TextCommandResult ChunkHeatmapCsv(string olderName, string newerName)
    {
        var sa = LoadSnap(olderName);
        var sb = LoadSnap(newerName);
        if (sa is null || sb is null)
            return TextCommandResult.Error("[MemLeakInspector] Snapshot(s) not found.");

        string outPath = Path.Combine(_snap.SnapshotDir, $"heatmap-{olderName}_to_{newerName}.csv");
        SnapshotCsv.WriteHeatmapCsv(sa, sb, outPath);
        return TextCommandResult.Success("[MemLeakInspector] Chunk heatmap CSV exported.");
    }

    // ------------------------------------------------------------------

    private MemSnapshot? LoadSnap(string name)
    {
        string? path = _snap.FindSnapshot(name);
        return path is null ? null : _snap.Load(path);
    }

    private TextCommandResult RunMaybeAsync(Func<TextCommandResult> work)
    {
        if (!_config.EnableAsyncCommands) return work();
        _ = Task.Run(() =>
        {
            var result = work();
            _sapi.Logger.Notification(result.StatusMessage);
        });
        return TextCommandResult.Success("[MemLeakInspector] Running in background...");
    }
}
