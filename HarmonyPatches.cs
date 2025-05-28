using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace MemLeakInspector
{
    /// <summary>
    /// Harmony patches used to automatically register entities and block entities for tracking.
    /// </summary>
    /// <remarks>
    /// These hooks ensure that memory snapshots capture runtime objects created by the game without requiring manual instrumentation.
    /// </remarks>
    [HarmonyPatch]
    public static class HarmonyPatches
    {
        /// <summary>
        /// Patch applied to <c>BlockEntity.Initialize</c> to automatically register block entities with the InstanceTracker.
        /// </summary>
        /// <param name="__instance">The block entity being initialized.</param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.Initialize))]
        public static void Postfix_BE_Initialize(BlockEntity __instance)
        {
            InstanceTracker.RegisterObject(__instance);
        }

        /// <summary>
        /// Patch applied to <c>Entity.OnGameTick</c> to register entities every tick.
        /// </summary>
        /// <param name="__instance">The entity being updated.</param>
        /// <remarks>
        /// This is resilient to mid-game spawns and does not rely on constructor injection.
        /// </remarks>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Entity), "OnGameTick")]
        public static void Postfix_Entity_OnGameTick(Entity __instance)
        {
            InstanceTracker.RegisterObject(__instance);
        }
    }
}
