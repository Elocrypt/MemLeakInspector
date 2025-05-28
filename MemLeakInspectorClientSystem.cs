using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace MemLeakInspector
{
    public class MemLeakInspectorClientSystem : ModSystem
    {
        private ICoreClientAPI capi = null!;

        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;

            capi.Network
                .RegisterChannel("memleakinspector")
                .RegisterMessageType<MemLeakInspectorHighlightPacket>()
                .SetMessageHandler<MemLeakInspectorHighlightPacket>(OnReceiveHighlightPacket);
        }

        private void OnReceiveHighlightPacket(MemLeakInspectorHighlightPacket packet)
        {
            List<BlockPos> all = new();
            foreach (var group in packet.Highlights)
                all.AddRange(group.Positions);

            List<int> colors = all.Select(_ => ColorUtil.ToRgba(255, 255, 0, 0)).ToList();

            capi.World.HighlightBlocks(
                capi.World.Player,
                99,
                all,
                colors,
                EnumHighlightBlocksMode.Absolute
            );
            capi.World.RegisterCallback(_ =>
            {
                capi.World.HighlightBlocks(capi.World.Player, 99, new List<BlockPos>(), new List<int>(), EnumHighlightBlocksMode.Absolute);
            }, 10000); // 10 sec
            capi.ShowChatMessage($"[MemLeakInspector] Highlighted {all.Count} leaking instances.");
        }
    }
}
