using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace MemLeakInspector.Core;

/// <summary>
/// Minimal client-side stub. Future versions may handle highlight rendering
/// or client-side UI panels.
/// </summary>
public class MemLeakInspectorClientSystem : ModSystem
{
    public override void StartClientSide(ICoreClientAPI capi) { /* no-op */ }
}
