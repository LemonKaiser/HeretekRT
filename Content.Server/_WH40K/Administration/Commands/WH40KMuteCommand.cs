using System;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server._WH40K.Administration.Mute;
using Content.Shared.Administration;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class WH40KMuteCommand : LocalizedCommands
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override string Command => "mute";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3 || args.Length > 5)
        {
            shell.WriteError(Help);
            return;
        }

        if (!TryParseMuteType(args[1], out var type))
        {
            shell.WriteError(Loc.GetString("wh40k-mute-command-invalid-type", ("type", args[1])));
            shell.WriteError(Help);
            return;
        }

        uint minutes = 0;
        if (args.Length >= 4 && !uint.TryParse(args[3], out minutes))
        {
            shell.WriteError(Loc.GetString("cmd-ban-invalid-minutes", ("minutes", args[3])));
            shell.WriteError(Help);
            return;
        }

        TimeSpan? duration = args.Length >= 4 && minutes != 0 ? TimeSpan.FromMinutes(minutes) : null;
        if (!WH40KMutePolicy.IsValidTemporaryDuration(duration))
        {
            shell.WriteError(Loc.GetString("wh40k-mute-command-invalid-duration"));
            return;
        }

        var erase = false;
        if (args.Length == 5 && !TryParseBool(args[4], out erase))
        {
            shell.WriteError(Loc.GetString("wh40k-mute-command-invalid-erase", ("value", args[4])));
            shell.WriteError(Help);
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("cmd-ban-player"));
            return;
        }

        if (await _adminActionGuard.TryDenyProtectedTargetAsync(
                shell.Player,
                located.UserId,
                Loc.GetString("wh40k-admin-hierarchy-action-mute"),
                located.Username,
                shell.WriteLine))
        {
            return;
        }

        var muteSystem = _entities.System<WH40KMuteSystem>();
        var result = await muteSystem.ApplyMuteAsync(
            located.UserId,
            located.Username,
            type,
            args[2],
            duration,
            shell.Player?.UserId,
            erase);
        if (result != WH40KMuteApplyResult.Applied)
        {
            shell.WriteError(muteSystem.GetApplyFailureMessage(result));
            return;
        }

        shell.WriteLine(Loc.GetString("wh40k-mute-command-success", ("player", located.Username)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                _playerManager.Sessions.Select(c => c.Name).OrderBy(c => c).ToArray(),
                Loc.GetString("cmd-ban-hint"));
        }

        if (args.Length == 2)
        {
            return CompletionResult.FromHintOptions(
                [
                    new CompletionOption("chat", Loc.GetString("wh40k-mute-scope-chat")),
                    new CompletionOption("ahelp", Loc.GetString("wh40k-mute-scope-ahelp")),
                    new CompletionOption("all", Loc.GetString("wh40k-mute-scope-all")),
                ],
                Loc.GetString("wh40k-mute-command-hint-scope"));
        }

        if (args.Length == 3)
            return CompletionResult.FromHint(Loc.GetString("cmd-ban-hint-reason"));
        if (args.Length == 4)
            return CompletionResult.FromHint(Loc.GetString("cmd-ban-hint-duration"));
        if (args.Length == 5)
            return CompletionResult.FromHintOptions(
                [
                    new CompletionOption("false", Loc.GetString("wh40k-mute-command-hint-erase-no")),
                    new CompletionOption("true", Loc.GetString("wh40k-mute-command-hint-erase-yes")),
                ],
                Loc.GetString("wh40k-mute-command-hint-erase"));

        return CompletionResult.Empty;
    }

    internal static bool TryParseMuteType(string raw, out WH40KMuteType type)
    {
        type = raw.ToLowerInvariant() switch
        {
            "chat" => WH40KMuteType.Chat,
            "ahelp" or "ah" => WH40KMuteType.AHelp,
            "all" or "both" => WH40KMuteType.Chat | WH40KMuteType.AHelp,
            _ => WH40KMuteType.None
        };
        return type != WH40KMuteType.None;
    }

    private static bool TryParseBool(string raw, out bool value)
    {
        switch (raw.ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "y":
                value = true;
                return true;
            case "false":
            case "0":
            case "no":
            case "n":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }
}
