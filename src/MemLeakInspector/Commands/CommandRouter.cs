using MemLeakInspector.Configuration;
using MemLeakInspector.Diagnostics;
using MemLeakInspector.Rendering;
using MemLeakInspector.Snapshots;
using MemLeakInspector.Tracking;
using Vintagestory.API.Server;

namespace MemLeakInspector.Commands;

/// <summary>
/// Registers the <c>/mem</c> command tree. Each feature area is handled by
/// a dedicated command class that receives only the services it needs.
/// </summary>
internal sealed class CommandRouter
{
    private readonly ICoreServerAPI _sapi;
    private readonly MemLeakInspectorConfig _config;

    // Command handler instances
    private readonly SnapshotCommands _snapCmds;
    private readonly WatcherCommands _watchCmds;
    private readonly DiagnosticCommands _diagCmds;
    private readonly HeatCommands _heatCmds;
    private readonly TrackingCommands _trackCmds;

    public CommandRouter(
        ICoreServerAPI sapi,
        MemLeakInspectorConfig config,
        InstanceTracker tracker,
        SnapshotService snapService,
        ThreadMonitor threadMonitor,
        AlertWatcher alertWatcher,
        RuntimeCounterListener? counters,
        HeatHighlighter? highlighter)
    {
        _sapi = sapi;
        _config = config;

        _snapCmds = new SnapshotCommands(sapi, config, snapService);
        _watchCmds = new WatcherCommands(sapi, config, tracker, snapService);
        _diagCmds = new DiagnosticCommands(sapi, config, threadMonitor, alertWatcher, counters, snapService);
        _heatCmds = new HeatCommands(sapi, config, snapService, highlighter);
        _trackCmds = new TrackingCommands(sapi, config, tracker);
    }

    /// <summary>Register the full <c>/mem</c> command tree with the VS chat command API.</summary>
    public void Register()
    {
        var root = _sapi.ChatCommands
            .Create("mem")
            .WithDescription("Memory debugging tools (MemLeakInspector 2.x)")
            .RequiresPrivilege("controlserver");

        _snapCmds.Register(root);
        _watchCmds.Register(root);
        _diagCmds.Register(root);
        _heatCmds.Register(root);
        _trackCmds.Register(root);

        _sapi.Logger.Notification("[MemLeakInspector] Chat commands registered.");
    }
}
