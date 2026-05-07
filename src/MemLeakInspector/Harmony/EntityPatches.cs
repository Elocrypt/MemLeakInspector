using HarmonyLib;
using MemLeakInspector.Tracking;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace MemLeakInspector.Harmony;

/// <summary>
/// Harmony postfix/prefix patches that hook Entity and BlockEntity lifecycle methods
/// to register and (optionally) unregister instances with the tracker.
/// </summary>
/// <remarks>
/// Harmony patches must be static. We reach the tracker through
/// <see cref="InstanceTracker.Current"/> which the ModSystem sets on startup.
/// </remarks>
[HarmonyPatch]
internal static class EntityPatches
{
    // --- BlockEntity.Initialize(ICoreAPI) ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.Initialize), [typeof(ICoreAPI)])]
    public static void Postfix_BE_Initialize(BlockEntity __instance, ICoreAPI api)
    {
        if (api.Side.IsServer())
            InstanceTracker.Current?.Register(__instance);
    }

    // --- Entity.Initialize(EntityProperties, ICoreAPI, long) ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Entity), nameof(Entity.Initialize),
        [typeof(EntityProperties), typeof(ICoreAPI), typeof(long)])]
    public static void Postfix_Entity_Initialize(Entity __instance,
        [HarmonyArgument("api")] ICoreAPI api)
    {
        if (api.Side.IsServer())
            InstanceTracker.Current?.Register(__instance);
    }

    // --- Entity.OnEntityDespawn(EntityDespawnData) ---

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Entity), nameof(Entity.OnEntityDespawn), [typeof(EntityDespawnData)])]
    public static void Prefix_Entity_OnDespawn(Entity __instance)
    {
        InstanceTracker.Current?.Unregister(__instance);
    }
}
