using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Asynchronous;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Server._WH40K.ShuttleStorage;

[AdminCommand(AdminFlags.Server)]
public sealed class ShuttleStorageCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private ITaskManager _tasks = default!;
    public string Command => "shuttlestorage";
    public string Description => "Inspect or resolve a personal shuttle hangar transfer.";
    public string Help => "shuttlestorage inspect <account UUID> | shuttlestorage <recover|restore|discard> <account UUID> <reason> | shuttlestorage return <account UUID> <console entity> <reason>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || !Guid.TryParse(args[1], out var account))
        { shell.WriteError(Help); return; }
        EntityUid? console = null;
        if (args[0] == "return")
        {
            if (args.Length < 4 || !EntityUid.TryParse(args[2], out var uid))
            { shell.WriteError(Help); return; }
            console = uid;
        }
        if (args[0] != "inspect" && (args[0] is not ("return" or "recover" or "restore" or "discard") || args.Length < 3))
        { shell.WriteError(Help); return; }
        _ = RunAsync(shell, args[0], account, console, string.Join(" ", args.Skip(console == null ? 2 : 3)));
    }

    private async Task RunAsync(IConsoleShell shell, string mode, Guid account, EntityUid? console, string reason)
    {
        try
        {
            var system = _entities.System<ShuttleStorageSystem>();
            if (mode == "inspect")
            {
                var row = await system.InspectAccountAsync(account);
                var text = row == null ? "No saved shuttle." :
                    $"{row.Owner}: {row.State}, revision {row.Revision}, operation {row.Operation}, authorized={row.Authorized}, ship={row.Ship}, {row.Width}x{row.Height}, {row.Bytes} bytes.";
                _tasks.RunOnMainThread(() => shell.WriteLine(text));
            }
            else
            {
                var result = await system.ResolveAsync(new NetUserId(account), mode, reason,
                    shell.Player?.UserId.ToString() ?? "server console", console);
                _tasks.RunOnMainThread(() => shell.WriteLine(result.ToString()));
            }
        }
        catch (Exception error) { _tasks.RunOnMainThread(() => shell.WriteError(error.Message)); }
    }
}
