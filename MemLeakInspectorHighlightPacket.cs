using Vintagestory.API.MathTools;

namespace MemLeakInspector
{
    public class MemLeakInspectorHighlightPacket
    {
        public List<HighlightGroup> Highlights { get; set; } = new();

        public class HighlightGroup
        {
            public string? Type { get; set; }
            public List<BlockPos> Positions { get; set; } = new();
        }
    }
}
