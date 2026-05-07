using MemLeakInspector.Configuration;
using MemLeakInspector.Tracking;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace MemLeakInspector.Commands;

/// <summary>
/// Handles tracking/filter commands: track allow, track deny, track show, tp.
/// </summary>
internal sealed class TrackingCommands
{
    private readonly ICoreServerAPI _sapi;
    private readonly MemLeakInspectorConfig _config;
    private readonly InstanceTracker _tracker;

    public TrackingCommands(ICoreServerAPI sapi, MemLeakInspectorConfig config, InstanceTracker tracker)
    {
        _sapi = sapi;
        _config = config;
        _tracker = tracker;
    }

    public void Register(IChatCommand root)
    {
        root.BeginSubCommand("tp")
            .WithDescription("Teleport to a tracked instance ID.")
            .WithArgs(_sapi.ChatCommands.Parsers.Word("id"))
            .HandleWith(ctx =>
            {
                var player = ctx.Caller.Player;
                var id = ctx.Parsers[0].GetValue() as string;
                if (string.IsNullOrWhiteSpace(id))
                    return TextCommandResult.Error("[MemLeakInspector] Missing instance ID.");

                var pos = _tracker.FindPositionById(id);
                if (pos is null)
                    return TextCommandResult.Error($"[MemLeakInspector] No instance with ID prefix '{id}'.");

                player.Entity.TeleportToDouble(pos.X + 0.5, pos.Y + 1, pos.Z + 0.5);
                return TextCommandResult.Success($"[MemLeakInspector] Teleported to {id}.");
            })
        .EndSubCommand();

        root.BeginSubCommand("track")
            .WithDescription("Manage allow/deny tracking lists.")
            .BeginSubCommand("allow")
                .WithArgs(_sapi.ChatCommands.Parsers.WordRange("regex"))
                .HandleWith(ctx =>
                {
                    string pattern = ctx.Parsers[0].GetValue()!.ToString()!;
                    _config.Tracking.AllowListRegex = [.. _config.Tracking.AllowListRegex, pattern];
                    _tracker.SetFilter(_config.Tracking);
                    _sapi.StoreModConfig(_config, "MemLeakInspectorConfig.json");
                    return TextCommandResult.Success("[MemLeakInspector] Allow pattern added.");
                })
            .EndSubCommand()
            .BeginSubCommand("deny")
                .WithArgs(_sapi.ChatCommands.Parsers.WordRange("regex"))
                .HandleWith(ctx =>
                {
                    string pattern = ctx.Parsers[0].GetValue()!.ToString()!;
                    _config.Tracking.DenyListRegex = [.. _config.Tracking.DenyListRegex, pattern];
                    _tracker.SetFilter(_config.Tracking);
                    _sapi.StoreModConfig(_config, "MemLeakInspectorConfig.json");
                    return TextCommandResult.Success("[MemLeakInspector] Deny pattern added.");
                })
            .EndSubCommand()
            .BeginSubCommand("show")
                .HandleWith(_ =>
                {
                    _sapi.Logger.Notification("[MemLeakInspector] Allow: " +
                        string.Join("; ", _config.Tracking.AllowListRegex));
                    _sapi.Logger.Notification("[MemLeakInspector] Deny: " +
                        string.Join("; ", _config.Tracking.DenyListRegex));
                    return TextCommandResult.Success("[MemLeakInspector] Tracking lists printed.");
                })
            .EndSubCommand()
        .EndSubCommand();
    }
}
