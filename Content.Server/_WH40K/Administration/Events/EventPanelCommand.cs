using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WH40K.Administration.Events;

[AdminCommand(AdminFlags.Fun)]
public sealed class EventPanelCommand : LocalizedCommands
{
    [Dependency] private EuiManager _euis = default!;
    public override string Command => "event_panel";
    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }
        if (args.Length != 0) { shell.WriteError(Help); return; }
        _euis.OpenEui(new EventAdminEui(), player);
    }
}
