using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server.Power.Generator;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._FarHorizons.Materials;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared._WH40K.ShipFuel;
using Content.Shared.Power.Generator;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ShipFuel;

public sealed partial class Wh40kShipFuelSystem : EntitySystem
{
    [Dependency] private GeneratorSystem _generator = default!;
    [Dependency] private AmeControllerSystem _ame = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, Wh40kShipFuelReading> _readings = new();
    private TimeSpan _nextSample;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextSample)
            return;

        _nextSample = _timing.CurTime + TimeSpan.FromSeconds(1);
        _readings.Clear();

        var activeHelms = EntityQueryEnumerator<ActiveUserInterfaceComponent, ShuttleConsoleComponent, UserInterfaceComponent>();
        while (activeHelms.MoveNext(out var helm, out _, out _, out var ui))
        {
            if (!_ui.IsUiOpen((helm, ui), ShuttleConsoleUiKey.Key))
                continue;

            var ev = new ConsoleShuttleEvent { Console = helm };
            RaiseLocalEvent(helm, ref ev);
            var reading = default(Wh40kShipFuelReading);
            if (TryComp<TransformComponent>(ev.Console, out var xform) && xform.GridUid is { } grid &&
                !_readings.TryGetValue(grid, out reading))
            {
                reading = ReadGrid(grid);
                _readings.Add(grid, reading);
            }

            _ui.ServerSendUiMessage((helm, ui), ShuttleConsoleUiKey.Key, new Wh40kShipFuelMessage(reading));
        }
    }

    public Wh40kShipFuelReading ReadGrid(EntityUid grid)
    {
        var sources = 0;
        var unknown = 0;
        var reserve = 0f;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (!Transform(uid).Anchored || TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                continue;

            float? fraction;
            if (HasComp<FuelGeneratorComponent>(uid))
                fraction = _generator.GetLocalFuelFraction(uid);
            else if (TryComp<AmeControllerComponent>(uid, out var controller))
                fraction = _ame.GetLocalFuelFraction(controller);
            else if (TryComp<NuclearReactorComponent>(uid, out var reactor))
                fraction = ReadReactor(reactor);
            else
                continue;

            sources++;
            if (fraction is { } known)
                reserve += known;
            else
                unknown++;
        }

        return new Wh40kShipFuelReading(sources > unknown ? reserve / (sources - unknown) : 0f, sources, unknown);
    }

    private float? ReadReactor(NuclearReactorComponent reactor)
    {
        if (reactor.Melted)
            return 0f;
        if (reactor.ComponentGrid == null)
            return null;

        var remaining = 0f;
        var capacity = 0f;
        foreach (var rod in reactor.ComponentGrid)
        {
            if (rod == null || !rod.HasRodType(ReactorPartComponent.RodTypes.FuelRod))
                continue;
            if (!_prototypes.TryIndex(rod.Material, out var material))
                return null;

            var fresh = FuelActivity(material.Properties);
            var current = FuelActivity(rod.Properties ?? material.Properties);
            if (!float.IsFinite(fresh) || !float.IsFinite(current))
                return null;

            capacity += fresh;
            if (!rod.Melted)
                remaining += Math.Clamp(current, 0f, fresh);
        }

        return capacity > 0f ? remaining / capacity : 0f;
    }

    private static float FuelActivity(MaterialProperties material)
    {
        // Each unit of neutron-active fuel also produces half a unit of ordinary active fuel.
        return 1.5f * Math.Max(0f, material.NeutronRadioactivity) + Math.Max(0f, material.Radioactivity);
    }
}
