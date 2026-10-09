using System.Linq;
using Content.Server.Administration;
using Content.Server._WH40K.VendingMachines.Systems;
using Content.Shared.Administration;
using Content.Shared.Station.Components;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.VendingMachines.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class AutoVendingRestockCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IEntitySystemManager _systems = default!;

    public string Command => "autovend_restock";
    public string Description => "Queues vending machines for an automatic-style restock.";
    public string Help => "autovend_restock [all|here|entity uid]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }

        var restock = _systems.GetEntitySystem<AutoVendingRestockSystem>();
        if (args.Length == 0 || args[0].Equals("here", StringComparison.OrdinalIgnoreCase))
        {
            if (shell.Player?.AttachedEntity is not { } attached || !TryGetGrid(attached, out var grid))
            {
                shell.WriteError("Stand on a station grid or provide an entity UID.");
                return;
            }

            shell.WriteLine($"Queued {restock.QueueMachinesOnGrid(grid)} vending machines on this grid.");
            return;
        }

        if (args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            shell.WriteLine($"Queued {restock.QueueConfiguredMachines()} vending machines on configured grids.");
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity) ||
            !_entities.TryGetEntity(netEntity, out var targetNullable) ||
            targetNullable is not { } target ||
            !TryGetGrid(target, out var targetGrid))
        {
            shell.WriteError("Invalid entity UID or entity is not on a station grid.");
            return;
        }

        shell.WriteLine($"Queued {restock.QueueMachinesOnGrid(targetGrid)} vending machines on the target grid.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["all", "here"], "target")
            : CompletionResult.Empty;
    }

    private bool TryGetGrid(EntityUid target, out EntityUid grid)
    {
        if (_entities.HasComponent<MapGridComponent>(target))
        {
            grid = target;
            return true;
        }

        if (_entities.TryGetComponent<StationDataComponent>(target, out var station))
        {
            grid = station.Grids.FirstOrDefault();
            return grid.IsValid();
        }

        var xform = _entities.GetComponent<TransformComponent>(target);
        grid = xform.GridUid ?? EntityUid.Invalid;
        return grid.IsValid();
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class AutoVendingRestockToggleCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IEntitySystemManager _systems = default!;

    public string Command => "autovend_schedule";
    public string Description => "Enables or disables scheduled vending restock on a grid.";
    public string Help => "autovend_schedule <on|off> [here|entity uid]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2 ||
            !args[0].Equals("on", StringComparison.OrdinalIgnoreCase) &&
            !args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            shell.WriteError(Help);
            return;
        }

        EntityUid target;
        if (args.Length == 1 || args[1].Equals("here", StringComparison.OrdinalIgnoreCase))
        {
            if (shell.Player?.AttachedEntity is not { } attached)
            {
                shell.WriteError("Stand on a station grid or provide an entity UID.");
                return;
            }

            target = attached;
        }
        else if (!NetEntity.TryParse(args[1], out var netEntity) ||
                 !_entities.TryGetEntity(netEntity, out var targetNullable) ||
                 targetNullable is not { } resolvedTarget)
        {
            shell.WriteError("Invalid entity UID.");
            return;
        }
        else
        {
            target = resolvedTarget;
        }

        if (!_entities.HasComponent<MapGridComponent>(target))
        {
            var transform = _entities.GetComponent<TransformComponent>(target);
            target = transform.GridUid ?? EntityUid.Invalid;
        }

        if (!target.IsValid() || !_entities.HasComponent<MapGridComponent>(target))
        {
            shell.WriteError("Target is not on a station grid.");
            return;
        }

        var enabled = args[0].Equals("on", StringComparison.OrdinalIgnoreCase);
        var restock = _systems.GetEntitySystem<AutoVendingRestockSystem>();
        if (!restock.SetGridEnabled(target, enabled))
        {
            shell.WriteError("Could not change the grid schedule.");
            return;
        }

        shell.WriteLine($"Scheduled vending restock {(enabled ? "enabled" : "disabled")} on grid {target}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) =>
        args.Length == 1
            ? CompletionResult.FromHintOptions(["on", "off"], "mode")
            : args.Length == 2
                ? CompletionResult.FromHintOptions(["here"], "target")
                : CompletionResult.Empty;
}
