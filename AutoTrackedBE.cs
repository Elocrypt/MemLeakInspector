using Vintagestory.API.Common;

namespace MemLeakInspector
{
    /// <summary>
    /// A custom block entity that auto-registers itself with the InstanceTracker on initialization.
    /// </summary>
    /// <remarks>
    /// Use this class as a base for block entities that must be tracked for memory snapshotting without requiring Harmony patches.
    /// </remarks>
    public class AutoTrackedBE : BlockEntity
    {
        /// <summary>
        /// Called when the block entity is initialized by the engine.
        /// Registers the instance with the tracker.
        /// </summary>
        /// <param name="api">Core API provided by the engine.</param>
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            InstanceTracker.RegisterObject(this);
        }
    }
}
