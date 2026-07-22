using System;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared._WH40K.DeathTransition;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.DeathTransition.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class GhostPermissionGrantCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _playerLocator = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "ghostpermgrant";
    public string Description => "Grants a limited ghost permission to a player.";
    public string Help => "Usage: ghostpermgrant <playerNameOrUserId> <uses> [duration|permanent]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TryParseGrantArguments(shell, args, out var playerIdentifier, out var uses, out var duration))
            return;

        var player = await _playerLocator.LookupIdByNameOrIdAsync(playerIdentifier);
        if (player == null)
        {
            shell.WriteError("Player not found.");
            return;
        }

        DateTime? expiresAt = duration == null ? null : DateTime.UtcNow + duration.Value;
        var permission = new GhostPermissionData(uses, expiresAt);
        await _systems.GetEntitySystem<GhostPermissionSystem>().SetPermissionAsync(player.UserId, permission);
        var durationText = expiresAt == null ? "permanent" : $"until {expiresAt:O}";
        shell.WriteLine($"Ghost permission granted to {player.Username}: {uses} use(s), {durationText}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), "<playerNameOrUserId>")
            : CompletionResult.Empty;
    }

    internal static bool TryParseGrantArguments(
        IConsoleShell shell,
        string[] args,
        out string playerIdentifier,
        out int uses,
        out TimeSpan? duration)
    {
        playerIdentifier = string.Empty;
        uses = 0;
        duration = null;

        if (args.Length is < 2 or > 3)
        {
            shell.WriteError("Expected a player, a positive number of uses, and optionally a duration.");
            return false;
        }

        if (!int.TryParse(args[1], out uses) || uses <= 0)
        {
            shell.WriteError("Uses must be a positive whole number.");
            return false;
        }

        if (args.Length == 3
            && !args[2].Equals("permanent", StringComparison.OrdinalIgnoreCase))
        {
            if (!TimeSpan.TryParse(args[2], out var parsedDuration) || parsedDuration <= TimeSpan.Zero)
            {
                shell.WriteError("Duration must be positive and use TimeSpan format, for example 01:30:00.");
                return false;
            }

            duration = parsedDuration;
        }

        playerIdentifier = args[0];
        return true;
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class GhostPermissionRevokeCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _playerLocator = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "ghostpermrevoke";
    public string Description => "Revokes a player's ghost permission.";
    public string Help => "Usage: ghostpermrevoke <playerNameOrUserId>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        var player = await _playerLocator.LookupIdByNameOrIdAsync(args[0]);
        if (player == null)
        {
            shell.WriteError("Player not found.");
            return;
        }

        await _systems.GetEntitySystem<GhostPermissionSystem>().RemovePermissionAsync(player.UserId);
        shell.WriteLine($"Ghost permission revoked for {player.Username}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), "<playerNameOrUserId>")
            : CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class GhostPermissionGetCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _playerLocator = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "ghostpermget";
    public string Description => "Shows a player's active ghost permission.";
    public string Help => "Usage: ghostpermget <playerNameOrUserId>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        var player = await _playerLocator.LookupIdByNameOrIdAsync(args[0]);
        if (player == null)
        {
            shell.WriteError("Player not found.");
            return;
        }

        var permission = await _systems.GetEntitySystem<GhostPermissionSystem>()
            .GetStoredPermissionAsync(player.UserId);
        if (permission == null)
        {
            shell.WriteLine($"{player.Username} has no active ghost permission.");
            return;
        }

        var durationText = permission.ExpiresAt == null ? "permanent" : $"until {permission.ExpiresAt:O}";
        shell.WriteLine($"{player.Username}: {permission.RemainingUses} use(s), {durationText}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), "<playerNameOrUserId>")
            : CompletionResult.Empty;
    }
}
