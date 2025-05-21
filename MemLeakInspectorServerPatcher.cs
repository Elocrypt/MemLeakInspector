using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace MemLeakInspector
{
    public class MemLeakInspectorServerPatcher : ModSystem
    {
        private static bool harmonyPatched = false;

        public override void StartServerSide(ICoreServerAPI api)
        {
            var harmony = new Harmony("memleakinspector.autotrack");

            if (!harmonyPatched)
            {
                harmony.PatchAll();
                harmonyPatched = true;
                api.Logger.Notification("[MemLeakInspector] Harmony patches applied.");
            }
            else
            {
                api.Logger.Warning("[MemLeakInspector] Skipping duplicate patch attempt.");
            }
        }

        public override void Dispose()
        {
            new Harmony("memleakinspector.autotrack").UnpatchAll("memleakinspector.autotrack");
        }
    }
}
