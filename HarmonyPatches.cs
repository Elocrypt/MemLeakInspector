using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace MemLeakInspector
{

    [HarmonyPatch]
    public static class HarmonyPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.Initialize))]
        public static void Postfix_BE_Initialize(BlockEntity __instance)
        {
            InstanceTracker.RegisterObject(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Entity), "OnGameTick")]
        public static void Postfix_Entity_OnGameTick(Entity __instance)
        {
            InstanceTracker.RegisterObject(__instance);
        }
    }
}
