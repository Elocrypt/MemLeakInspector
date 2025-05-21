using Vintagestory.API.Common;

namespace MemLeakInspector
{
    public class AutoTrackedBE : BlockEntity
    {
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            InstanceTracker.RegisterObject(this);
        }
    }
}
