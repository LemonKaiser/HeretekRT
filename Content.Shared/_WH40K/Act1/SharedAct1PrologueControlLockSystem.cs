using Content.Shared.ActionBlocker;
using Content.Shared.Emoting;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Speech;
using Content.Shared.Throwing;

namespace Content.Shared._WH40K.Act1;

/// <summary>
/// Unlike dialogue locking, this deliberately leaves movement and facing available during exploration.
/// </summary>
public abstract partial class SharedAct1PrologueControlLockSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private PullingSystem _pulling = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Act1PrologueControlLockComponent, UseAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, PickupAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, ThrowAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, DropAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, AttackAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, PullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, SpeakAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, EmoteAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, IsEquippingAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, IsUnequippingAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, ComponentShutdown>(UpdateCanMove);
        SubscribeLocalEvent<Act1PrologueControlLockComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
    }

    public void SetBlockMovement(EntityUid uid, bool blockMovement)
    {
        var component = EnsureComp<Act1PrologueControlLockComponent>(uid);
        if (component.BlockMovement == blockMovement)
            return;

        component.BlockMovement = blockMovement;
        Dirty(uid, component);
        _actionBlocker.UpdateCanMove(uid);
    }

    private static void OnAttempt(
        EntityUid uid,
        Act1PrologueControlLockComponent component,
        CancellableEntityEventArgs args)
    {
        args.Cancel();
    }

    private static void OnInteractionAttempt(
        Entity<Act1PrologueControlLockComponent> ent,
        ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private static void OnPullAttempt(
        Entity<Act1PrologueControlLockComponent> ent,
        ref PullAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnStartup(
        EntityUid uid,
        Act1PrologueControlLockComponent component,
        ComponentStartup args)
    {
        if (TryComp<PullableComponent>(uid, out var pullable))
            _pulling.TryStopPull(uid, pullable);

        UpdateCanMove(uid, component, args);
    }

    private static void OnUpdateCanMove(
        EntityUid uid,
        Act1PrologueControlLockComponent component,
        UpdateCanMoveEvent args)
    {
        if (component.LifeStage <= ComponentLifeStage.Running && component.BlockMovement)
            args.Cancel();
    }

    private void UpdateCanMove(
        EntityUid uid,
        Act1PrologueControlLockComponent component,
        EntityEventArgs args)
    {
        _actionBlocker.UpdateCanMove(uid);
    }
}
