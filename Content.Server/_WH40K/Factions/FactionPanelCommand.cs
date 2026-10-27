using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WH40K.Factions;

[AdminCommand(AdminFlags.Admin)]
public sealed class FactionPanelCommand : LocalizedCommands
{
    [Dependency] private EuiManager _euis = default!;
    public override string Command => "faction_panel";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }
        _euis.OpenEui(new FactionAdminEui(), player);
    }
}
