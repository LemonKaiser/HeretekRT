using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._Mono.LongRun;

/// <summary>
/// Displays the latest bounded long-run health snapshot without mutating the world.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class LongRunStatusCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "longrunstatus";
    public string Description => "Показывает диагностический снимок длительного раунда.";
    public string Help => Command;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine(_entities.System<LongRunHealthSystem>().GetStatus());
    }
}
