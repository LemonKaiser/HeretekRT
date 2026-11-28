using Content.Shared.ActionBlocker;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Holds a wearer motionless inside the Corona protective shield while the ring of orbs arms.
/// </summary>
public sealed partial class Wh40kHaloArmingSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private PullingSystem _pulling = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kHaloArmingComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kHaloArmingComponent, ComponentShutdown>((uid, _, _) => _blocker.UpdateCanMove(uid));
        SubscribeLocalEvent<Wh40kHaloArmingComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
    }

    private void OnStartup(Entity<Wh40kHaloArmingComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<PullableComponent>(ent, out var pullable))
            _pulling.TryStopPull(ent, pullable);

        _blocker.UpdateCanMove(ent);
    }

    private void OnUpdateCanMove(Entity<Wh40kHaloArmingComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.Cancel();
    }
}
