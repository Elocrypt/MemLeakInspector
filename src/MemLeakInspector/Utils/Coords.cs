using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace MemLeakInspector.Utils;

/// <summary>
/// Converts between absolute ("TRUE") world coordinates and HUD coordinates.
/// HUD origin is the world axis center (default spawn) = MapSize / 2 for X/Z.
/// </summary>
internal static class Coords
{
    private static bool _ready;
    private static int _cx, _cz;

    public static bool IsReady => _ready;
    public static (int X, int Z) Center => (_cx, _cz);

    public static void Init(ICoreServerAPI sapi)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        _cx = sapi.WorldManager.MapSizeX / 2;
        _cz = sapi.WorldManager.MapSizeZ / 2;
        _ready = true;
    }

    public static Vec3i ToHud(BlockPos abs)
    {
        if (!_ready) throw new InvalidOperationException("Coords.Init() not called.");
        return new Vec3i(abs.X - _cx, abs.Y, abs.Z - _cz);
    }

    public static Vec3i ToHud(Vec3d abs)
    {
        if (!_ready) throw new InvalidOperationException("Coords.Init() not called.");
        return new Vec3i((int)Math.Floor(abs.X) - _cx, (int)Math.Floor(abs.Y), (int)Math.Floor(abs.Z) - _cz);
    }

    public static BlockPos ToTrue(Vec3i hud)
    {
        if (!_ready) throw new InvalidOperationException("Coords.Init() not called.");
        return new BlockPos(hud.X + _cx, hud.Y, hud.Z + _cz);
    }

    public static void Reset() { _ready = false; _cx = _cz = 0; }
}
