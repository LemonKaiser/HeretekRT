using Content.Server._NF.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._NF.Shuttles.Systems;

public sealed partial class ForceAnchorSystem : EntitySystem
{
    [Dependency] PhysicsSystem _physics = default!;
    [Dependency] ShuttleSystem _shuttle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForceAnchorComponent, MapInitEvent>(OnForceAnchorMapInit);
        SubscribeLocalEvent<ForceAnchorPostFTLComponent, FTLCompletedEvent>(OnForceAnchorPostFTLCompleted);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnConsoleFTLAttempt, before: new[] { typeof(ShuttleSystem) });
    }

    /// <summary>
    /// Prevents grids with ForceAnchor component from using FTL travel
    /// </summary>
    private void OnConsoleFTLAttempt(ref ConsoleFTLAttemptEvent args)
    {
        // Only proceed if this event isn't already cancelled
        if (args.Cancelled)
            return;

        // Check if the entity trying to FTL has a ForceAnchorComponent
        if (HasComp<ForceAnchorComponent>(args.Uid))
        {
            args.Cancelled = true;
            args.Reason = Loc.GetString("shuttle-console-force-anchored");
        }
    }

    private void OnForceAnchorMapInit(Entity<ForceAnchorComponent> ent, ref MapInitEvent args)
    {
        EnsureGridAnchored(ent);
    }

    private void OnForceAnchorPostFTLCompleted(Entity<ForceAnchorPostFTLComponent> ent, ref FTLCompletedEvent args)
    {
        EnsureGridAnchored(ent);
    }

    /// <summary>
    /// Immediately anchors a grid and prevents a shuttle console from changing that state.
    /// This is also used by systems which add <see cref="ForceAnchorComponent"/> after map initialization.
    /// </summary>
    public void EnsureGridAnchored(EntityUid grid)
    {
        _shuttle.Disable(grid, force: true);
        EnsureComp<PreventGridAnchorChangesComponent>(grid);
    }
}
