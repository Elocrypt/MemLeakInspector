using System.Text.Json.Serialization;
using MemLeakInspector.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace MemLeakInspector.Tracking;

/// <summary>
/// Lightweight metadata for a tracked instance: ID string, block position, and HUD position.
/// </summary>
public sealed class InstanceInfo
{
    public string Id { get; set; } = "";

    [JsonIgnore]
    public BlockPos? Pos { get; set; }

    /// <summary>Serializable flat position (TRUE coords).</summary>
    public FlatPos? Position
    {
        get => Pos is null ? null : new FlatPos(Pos.X, Pos.Y, Pos.Z);
        set => Pos = value is null ? null : new BlockPos(value.Value.X, value.Value.Y, value.Value.Z);
    }

    /// <summary>HUD-relative position (offset from world center).</summary>
    public FlatPos? Hud { get; set; }

    /// <summary>Build an <see cref="InstanceInfo"/> from a live object.</summary>
    public static InstanceInfo FromObject(object obj)
    {
        var info = new InstanceInfo();
        switch (obj)
        {
            case Entity entity:
                info.Id = entity.EntityId.ToString();
                info.Pos = entity.Pos?.AsBlockPos;
                if (entity.Code is not null) info.Id += $" [{entity.Code.Path}]";
                break;

            case BlockEntity be:
                info.Id = be.Pos?.ToString() ?? "?";
                info.Pos = be.Pos;
                if (be.Block?.Code is not null) info.Id += $" [{be.Block.Code.Path}]";
                break;

            default:
                info.Id = obj.GetHashCode().ToString();
                break;
        }

        if (info.Pos is not null && Coords.IsReady)
        {
            var hud = Coords.ToHud(info.Pos);
            info.Hud = new FlatPos(hud.X, hud.Y, hud.Z);
        }

        return info;
    }
}

/// <summary>Simple serializable 3D integer position.</summary>
public record struct FlatPos(int X, int Y, int Z);
