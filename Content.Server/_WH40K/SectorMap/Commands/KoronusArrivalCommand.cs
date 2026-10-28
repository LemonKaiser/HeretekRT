using System.Linq;
using System.Globalization;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Localizations;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class KoronusArrivalCommand : IConsoleCommand
{
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEntityManager _entities = default!;

    public string Command => "koronus_arrival";
    public string Description => Loc.GetString("koronus-arrival-command-description");
    public string Help => Loc.GetString("koronus-arrival-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        using var scope = shell.Player is { } player
            ? _systems.GetEntitySystem<WH40KPlayerCultureManager>().CreateScope(player)
            : (LocalizationCultureScope?) null;
        var arrivals = _systems.GetEntitySystem<KoronusArrivalSystem>();
        if (args.Length == 0)
        {
            foreach (var (key, state) in arrivals.States)
            {
                shell.WriteLine(Loc.GetString("koronus-arrival-command-status", ("system", key.System), ("type", key.Kind.ToString()),
                    ("next", Remaining(state.NextAt)), ("arrival", Remaining(state.ArrivalAt)),
                    ("departure", Remaining(state.DepartureAt))));
                foreach (var grid in state.Grids)
                {
                    if (!_entities.TryGetComponent(grid, out TransformComponent? transform))
                        continue;
                    var position = _systems.GetEntitySystem<SharedTransformSystem>().GetWorldPosition(transform);
                    shell.WriteLine(Loc.GetString("koronus-arrival-command-grid", ("grid", grid.ToString()),
                        ("map", transform.MapID), ("position", position)));
                }
            }
            return;
        }

        float? lifetime = null;
        if (args.Length > 4 || args.Length > 1 && args[1] is not ("object" or "tau"))
        {
            shell.WriteError(Help);
            return;
        }
        var selector = args.Length > 2 ? args[2] : null;
        if (args.Length == 4 || args.Length == 3 && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            if (!float.TryParse(args[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                shell.WriteError(Help);
                return;
            }
            lifetime = parsed;
            if (args.Length == 3)
                selector = null;
        }

        var tau = args.Length > 1 && args[1] == "tau";
        var template = args.Length > 1 ? tau ? "KoronusTauDroneArrivals" : "KoronusObjectArrivals" : null;
        if (!arrivals.TryStart(args[0], tau ? null : selector, lifetime, template, tau ? selector : null))
        {
            shell.WriteError(Loc.GetString("koronus-arrival-command-failed"));
            return;
        }

        var started = arrivals.States.Where(pair => pair.Key.System == args[0] && pair.Value.ArrivalAt != null)
            .OrderBy(pair => pair.Value.LeaseId).First().Value;
        shell.WriteLine(Loc.GetString("koronus-arrival-command-started", ("system", args[0]),
            ("type", started.Settings.Kind.ToString()), ("seconds", Remaining(started.ArrivalAt)),
            ("lifetime", Math.Ceiling(started.Lifetime))));
    }

    private string Remaining(TimeSpan? deadline) => deadline is { } time
        ? Math.Ceiling(Math.Max(0, (time - _timing.CurTime).TotalSeconds)).ToString(CultureInfo.InvariantCulture)
        : "-";

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => CompletionResult.Empty;
}
