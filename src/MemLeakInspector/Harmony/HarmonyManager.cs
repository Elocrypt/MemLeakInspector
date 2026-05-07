using HarmonyLib;
using Vintagestory.API.Server;

namespace MemLeakInspector.Harmony;

/// <summary>
/// Owns the single <see cref="HarmonyLib.Harmony"/> instance for the mod.
/// Applies all patches exactly once and cleanly removes them on dispose.
/// </summary>
internal sealed class HarmonyManager : IDisposable
{
    private const string HarmonyId = "memleakinspector.2x";

    private readonly ICoreServerAPI _sapi;
    private HarmonyLib.Harmony? _harmony;
    private bool _disposed;

    public HarmonyManager(ICoreServerAPI sapi)
    {
        _sapi = sapi ?? throw new ArgumentNullException(nameof(sapi));
    }

    /// <summary>
    /// Apply all Harmony patches defined in the assembly. Safe to call multiple
    /// times — subsequent calls are no-ops.
    /// </summary>
    public void Apply()
    {
        if (_harmony is not null) return; // already patched

        try
        {
            HarmonyLib.Harmony.DEBUG = false;
            _harmony = new HarmonyLib.Harmony(HarmonyId);
            _harmony.PatchAll(typeof(HarmonyManager).Assembly);
            _sapi.Logger.Notification("[MemLeakInspector] Harmony patches applied.");
        }
        catch (Exception ex)
        {
            _sapi.Logger.Error($"[MemLeakInspector] Harmony patch failed: {ex.Message}");
            // Allow mod to continue without patches — tracking still works
            // via AutoTrackedBE and manual registration.
            _harmony = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_harmony is not null)
        {
            try
            {
                _harmony.UnpatchAll(HarmonyId);
                _sapi.Logger.Notification("[MemLeakInspector] Harmony patches removed.");
            }
            catch (Exception ex)
            {
                _sapi.Logger.Warning($"[MemLeakInspector] Harmony unpatch error: {ex.Message}");
            }

            _harmony = null;
        }
    }
}
