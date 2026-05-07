using Vintagestory.API.MathTools;

namespace MemLeakInspector.Rendering;

/// <summary>Network packet carrying highlight positions grouped by type.</summary>
internal sealed class HighlightPacket
{
    public List<HighlightGroup> Highlights { get; set; } = [];

    internal sealed class HighlightGroup
    {
        public string? Type { get; set; }
        public List<BlockPos> Positions { get; set; } = [];
    }
}
