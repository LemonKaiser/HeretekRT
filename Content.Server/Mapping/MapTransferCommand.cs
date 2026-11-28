using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Mapping;

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class MapTransferCommand : IConsoleCommand
{
    [Dependency] private MapTransferManager _transfers = default!;

    public string Command => "maptransfer";
    public string Description => Loc.GetString("cmd-map-transfer-desc");
    public string Help => Loc.GetString("cmd-map-transfer-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
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

        if (!_transfers.TryOpenEui(player))
            shell.WriteError(Loc.GetString("cmd-map-transfer-unavailable"));
    }
}
