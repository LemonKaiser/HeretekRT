using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WH40K.PersistentInventory.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class PersistentInventoryPanelCommand : LocalizedCommands
{
    [Dependency] private EuiManager _euis = default!;

    public override string Command => "persistentinv_panel";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }
        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }
        var panel = new PersistentInventoryAdminEui();
        _euis.OpenEui(panel, player);
        if (args.Length == 1)
            panel.SelectPlayer(args[0]);
    }
}
