using Content.Server.Administration.Logs;
using Content.Server.Electrocution;
using Content.Server.Particles;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using CableCuttingFinishedEvent = Content.Shared.Tools.Systems.CableCuttingFinishedEvent;
using SharedToolSystem = Content.Shared.Tools.Systems.SharedToolSystem;

namespace Content.Server.Power.EntitySystems;

public sealed partial class CableSystem : EntitySystem
{
    [Dependency] private ITileDefinitionManager _tileManager = default!;
    [Dependency] private SharedToolSystem _toolSystem = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private ElectrocutionSystem _electrocutionSystem = default!;
    [Dependency] private ParticleSpawnSystem _particles = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeCablePlacer();

        SubscribeLocalEvent<CableComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CableComponent, CableCuttingFinishedEvent>(OnCableCut);
        // Shouldn't need re-anchoring.
        SubscribeLocalEvent<CableComponent, AnchorStateChangedEvent>(OnAnchorChanged);
    }

    private void OnInteractUsing(EntityUid uid, CableComponent cable, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (cable.CuttingQuality != null)
        {
            args.Handled = _toolSystem.UseTool(args.Used, args.User, uid, cable.CuttingDelay, new[] { cable.CuttingQuality.Value }, new CableCuttingFinishedEvent());
        }
    }

    private void OnCableCut(EntityUid uid, CableComponent cable, DoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        var xform = Transform(uid);
        var ev = new CableAnchorStateChangedEvent(xform);
        RaiseLocalEvent(uid, ref ev);

        if (_electrocutionSystem.TryDoElectrifiedAct(uid, args.User))
        {
            _particles.Spawn(uid, "HrtElectricArcBurst", cooldown: TimeSpan.FromMilliseconds(100));
            return;
        }

        _particles.Spawn(uid, "HrtMetalChips", cooldown: TimeSpan.FromMilliseconds(100));

        _adminLogger.Add(LogType.CableCut, LogImpact.Medium, $"The {ToPrettyString(uid)} at {xform.Coordinates} was cut by {ToPrettyString(args.User)}.");

        Spawn(cable.CableDroppedOnCutPrototype, xform.Coordinates);
        QueueDel(uid);
    }

    private void OnAnchorChanged(EntityUid uid, CableComponent cable, ref AnchorStateChangedEvent args)
    {
        var ev = new CableAnchorStateChangedEvent(args.Transform, args.Detaching);
        RaiseLocalEvent(uid, ref ev);

        if (args.Anchored)
            return; // huh? it wasn't anchored?

        // anchor state can change as a result of deletion (detach to null).
        // We don't want to spawn an entity when deleted.
        if (TerminatingOrDeleted(uid))
            return;

        // This entity should not be un-anchorable. But this can happen if the grid-tile is deleted (RCD, explosion,
        // etc). In that case: behave as if the cable had been cut.
        Spawn(cable.CableDroppedOnCutPrototype, Transform(uid).Coordinates);
        QueueDel(uid);
    }
}
