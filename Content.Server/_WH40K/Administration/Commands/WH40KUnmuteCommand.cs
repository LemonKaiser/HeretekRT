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
public sealed partial class WH40KUnmuteCommand : LocalizedCommands
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override string Command => "unmute";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Help);
            return;
        }

        var type = WH40KMuteType.Chat | WH40KMuteType.AHelp;
        if (args.Length == 2 && !WH40KMuteCommand.TryParseMuteType(args[1], out type))
        {
            shell.WriteError(Loc.GetString("wh40k-mute-command-invalid-type", ("type", args[1])));
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
                Loc.GetString("wh40k-admin-hierarchy-action-unmute"),
                located.Username,
                shell.WriteLine))
        {
            return;
        }

        var result = await _entities.System<WH40KMuteSystem>().RemoveMuteAsync(
            located.UserId,
            type,
            shell.Player,
            shell.WriteError);
        if (!result.Allowed)
            return;

        shell.WriteLine(result.RemovedCount == 0
            ? Loc.GetString("wh40k-unmute-command-none-active", ("player", located.Username))
            : Loc.GetString("wh40k-unmute-command-success", ("player", located.Username), ("count", result.RemovedCount)));
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
                    new CompletionOption("all", Loc.GetString("wh40k-mute-scope-all")),
                    new CompletionOption("chat", Loc.GetString("wh40k-mute-scope-chat")),
                    new CompletionOption("ahelp", Loc.GetString("wh40k-mute-scope-ahelp")),
                ],
                Loc.GetString("wh40k-mute-command-hint-scope"));
        }

        return CompletionResult.Empty;
    }
}
