using MemLeakInspector.Tracking;
using Vintagestory.API.Common;

namespace MemLeakInspector.Core;

/// <summary>
/// A custom block entity that auto-registers itself with the tracker on initialization.
/// Use as a base class for BEs that must be tracked without requiring Harmony patches.
/// </summary>
public class AutoTrackedBE : BlockEntity
{
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side.IsServer())
            InstanceTracker.Current?.Register(this);
    }
}
