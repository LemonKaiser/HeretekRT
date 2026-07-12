using Content.Shared.ActionBlocker;
using Content.Shared.Emoting;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Speech;
using Content.Shared.Throwing;

namespace Content.Shared._WH40K.Dialogue;

public abstract class SharedDialogueInputLockSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private PullingSystem _pulling = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DialogueInputLockComponent, UseAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, PickupAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, ThrowAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, DropAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, AttackAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, ChangeDirectionAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, InteractionAttemptEvent>(OnInteractAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, PullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, SpeakAttemptEvent>(OnSpeakAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, EmoteAttemptEvent>(OnEmoteAttempt);
        SubscribeLocalEvent<DialogueInputLockComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<DialogueInputLockComponent, ComponentShutdown>(UpdateCanMove);
        SubscribeLocalEvent<DialogueInputLockComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
    }

    private void OnInteractAttempt(Entity<DialogueInputLockComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnAttempt(EntityUid uid, DialogueInputLockComponent component, CancellableEntityEventArgs args)
    {
        args.Cancel();
    }

    private void OnPullAttempt(EntityUid uid, DialogueInputLockComponent component, PullAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnSpeakAttempt(EntityUid uid, DialogueInputLockComponent component, SpeakAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnEmoteAttempt(EntityUid uid, DialogueInputLockComponent component, EmoteAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnStartup(EntityUid uid, DialogueInputLockComponent component, ComponentStartup args)
    {
        if (TryComp<PullableComponent>(uid, out var pullable))
            _pulling.TryStopPull(uid, pullable);

        UpdateCanMove(uid, component, args);
    }

    private void OnUpdateCanMove(EntityUid uid, DialogueInputLockComponent component, UpdateCanMoveEvent args)
    {
        if (component.LifeStage > ComponentLifeStage.Running)
            return;

        args.Cancel();
    }

    private void UpdateCanMove(EntityUid uid, DialogueInputLockComponent component, EntityEventArgs args)
    {
        _actionBlocker.UpdateCanMove(uid);
    }
}
