using System.Globalization;
using System.Text;
using MemLeakInspector.Tracking;
using MemLeakInspector.Utils;
using Vintagestory.API.MathTools;

namespace MemLeakInspector.Snapshots;

/// <summary>CSV export helpers for snapshot and heatmap data.</summary>
internal static class SnapshotCsv
{
    private static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny([',', '"', '\n', '\r']) >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    /// <summary>Per-instance CSV: type, id, TRUE (x/y/z), HUD (x/y/z).</summary>
    public static void WriteInstancesCsv(MemSnapshot snap, string path)
    {
        var sb = new StringBuilder(1 << 20);
        sb.AppendLine("type,id,trueX,trueY,trueZ,hudX,hudY,hudZ");

        if (snap.TrackedInstancesByType is not null)
        {
            foreach (var kv in snap.TrackedInstancesByType.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                foreach (var info in kv.Value)
                {
                    sb.Append(Esc(kv.Key)).Append(',');
                    sb.Append(Esc(info.Id)).Append(',');

                    var tp = info.Position;
                    if (tp.HasValue)
                    {
                        sb.Append(tp.Value.X.ToString(CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(tp.Value.Y.ToString(CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(tp.Value.Z.ToString(CultureInfo.InvariantCulture)).Append(',');
                    }
                    else sb.Append(",,,");

                    var hp = info.Hud;
                    if (hp.HasValue)
                    {
                        sb.Append(hp.Value.X.ToString(CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(hp.Value.Y.ToString(CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(hp.Value.Z.ToString(CultureInfo.InvariantCulture));
                    }
                    else sb.Append(",,");
                    sb.AppendLine();
                }
            }
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>Chunk growth CSV between two snapshots.</summary>
    public static void WriteHeatmapCsv(MemSnapshot oldSnap, MemSnapshot newSnap, string path)
    {
        var deltas = SnapshotDiff.DiffChunks(oldSnap, newSnap)
            .Where(kv => kv.Value != 0)
            .OrderByDescending(kv => kv.Value)
            .ToList();

        var sb = new StringBuilder(1 << 20);
        sb.AppendLine("chunkCx,chunkCy,chunkCz,trueX,trueY,trueZ,hudX,hudY,hudZ,delta");

        foreach (var (key, delta) in deltas)
        {
            short cx = (short)((key >> 16) & 0xFFFF);
            short cy = (short)((key >> 32) & 0xFFFF);
            short cz = (short)(key & 0xFFFF);

            var center = new BlockPos(cx * 32 + 16, cy * 32 + 16, cz * 32 + 16);
            var hud = Coords.ToHud(center);

            sb.Append(cx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(cy.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(cz.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(center.X.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(center.Y.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(center.Z.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(hud.X.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(hud.Y.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(hud.Z.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(delta.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
}
