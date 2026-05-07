using MemLeakInspector.Snapshots;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace MemLeakInspector.Rendering;

/// <summary>
/// Sends per-chunk highlight overlays to online players showing where
/// instance growth is concentrated. Rate-limited and distance-culled.
/// </summary>
internal sealed class HeatHighlighter
{
    private readonly ICoreServerAPI _sapi;
    private readonly int _cooldownSec, _maxDist, _topN;
    private DateTime _lastSent = DateTime.MinValue;
    private const int HighlightId = 2712;

    public HeatHighlighter(ICoreServerAPI sapi, int cooldownSec, int maxDistMeters, int topChunks)
    {
        _sapi = sapi;
        _cooldownSec = cooldownSec;
        _maxDist = maxDistMeters;
        _topN = topChunks;
    }

    public void MaybeSend(MemSnapshot prev, MemSnapshot cur)
    {
        if ((DateTime.UtcNow - _lastSent).TotalSeconds < _cooldownSec) return;
        _lastSent = DateTime.UtcNow;

        var growth = SnapshotDiff.DiffChunks(prev, cur)
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(_topN)
            .ToList();

        if (growth.Count == 0) return;

        foreach (var sp in _sapi.World.AllOnlinePlayers)
        {
            var ppos = sp.Entity?.Pos?.XYZ ?? new Vec3d(0, 0, 0);
            var blocks = new List<BlockPos>();
            var colors = new List<int>();

            foreach (var (chunkIdx, delta) in growth)
            {
                int cx = (int)((chunkIdx >> 16) & 0xFFFF);
                int cy = (int)((chunkIdx >> 32) & 0xFFFF);
                int cz = (int)(chunkIdx & 0xFFFF);
                var wpos = new BlockPos(cx * 32 + 16, cy * 32 + 16, cz * 32 + 16);

                if (wpos.DistanceSqTo(ppos.X, ppos.Y, ppos.Z) > (_maxDist * _maxDist))
                    continue;

                blocks.Add(wpos);
                colors.Add(ColorFromDelta(delta));
            }

            if (blocks.Count > 0)
            {
                _sapi.World.HighlightBlocks(sp, HighlightId, blocks, colors);
            }
        }
    }

    private static int ColorFromDelta(int d)
    {
        d = Math.Clamp(d, 1, 255);
        int a = 80, r = 50 + Math.Min(205, d), g = 20, b = 240 - Math.Min(240, d);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
