using Content.Shared.Administration;
using Robust.Shared.Console;
using Content.Server.EUI;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Ban)]
public sealed partial class BanPanelCommand : LocalizedCommands
{

    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private EuiManager _euis = default!;
    [Dependency] private Content.Server.Administration.Managers.IAdminAuthorizationManager _authorization = default!;

    public override string Command => "banpanel";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        switch (args.Length)
        {
            case 0:
                _euis.OpenEui(new BanPanelEui(), player);
                break;
            case 1:
                var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
                if (located is null)
                {
                    shell.WriteError(Loc.GetString("cmd-banpanel-player-err"));
                    return;
                }
                if (await _authorization.TryDenyTargetAsync(
                        player,
                        located.UserId,
                        Content.Server.Administration.Managers.AdminOperation.Ban,
                        located.Username,
                        shell.WriteError))
                {
                    return;
                }
                var ui = new BanPanelEui();
                _euis.OpenEui(ui, player);
                ui.ChangePlayer(located.UserId, located.Username, located.LastAddress, located.LastHWId);
                break;
            default:
                shell.WriteLine(Loc.GetString("cmd-ban-invalid-arguments"));
                shell.WriteLine(Help);
                return;
        }
    }
}
