using Content.Server.Administration;
using Content.Server.Worldgen.Systems.GC;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Displays bounded cleanup queues and lifetime counters without mutating the world.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class CleanupStatusCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "cleanupstatus";
    public string Description => "Показывает состояние автоматических систем очистки.";
    public string Help => Command;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine(_entities.System<GarbageCleanupSystem>().GetCleanupStatus());
        shell.WriteLine(_entities.System<SpaceCleanupSystem>().GetCleanupStatus());
        shell.WriteLine(_entities.System<GridCleanupSystem>().GetCleanupStatus());
        shell.WriteLine(_entities.System<MobCleanupSystem>().GetCleanupStatus());
        shell.WriteLine(_entities.System<DecalCleanupSystem>().GetCleanupStatus());
        shell.WriteLine(_entities.System<GCQueueSystem>().GetCleanupStatus());
    }
}
