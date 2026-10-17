using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WH40K.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kPlayerResetCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private ILogManager _logs = default!;

    public string Command => "wh40kwipe";
    public string Description => Loc.GetString("wh40k-player-reset-description");
    public string Help => Loc.GetString("wh40k-player-reset-help");

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteError(Help);
            return;
        }
        try
        {
            var player = await _locator.LookupIdByNameOrIdAsync(args[0]);
            if (player == null)
            {
                shell.WriteError(Loc.GetString("wh40k-player-reset-not-found"));
                return;
            }
            var reason = args.Length == 1
                ? Loc.GetString("wh40k-player-reset-default-reason")
                : string.Join(' ', args.Skip(1)).Trim();
            if (reason.Length is < 1 or > 512)
            {
                shell.WriteError(Help);
                return;
            }
            shell.WriteLine(Loc.GetString("wh40k-player-reset-start", ("player", player.Username)));
            var success = await _entities.System<Wh40kPlayerResetSystem>()
                .ResetAsync(player.UserId, player.Username, shell.Player, reason);
            if (success)
                shell.WriteLine(Loc.GetString("wh40k-player-reset-success", ("player", player.Username)));
            else
                shell.WriteError(Loc.GetString("wh40k-player-reset-failed"));
        }
        catch (Exception error)
        {
            _logs.GetSawmill("wh40k.player-reset").Error($"Player reset command failed: {error}");
            shell.WriteError(Loc.GetString("wh40k-player-reset-failed"));
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        => args.Length == 1 ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), "<player|UserId>")
            : CompletionResult.Empty;
}
