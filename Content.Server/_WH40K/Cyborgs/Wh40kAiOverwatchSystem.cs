using Content.Shared._Rat.Overwatch;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Enables Station AIs to watch Overwatch camera feeds cleanly: caps feed zoom, cancels feed when the AI's
/// remote eye moves, and restores the AI's eye target to its remote eye afterwards.
/// </summary>
public sealed partial class Wh40kAiOverwatchSystem : EntitySystem
{
    [Dependency] private SharedContentEyeSystem _contentEye = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OverwatchConsoleComponent, BoundUserInterfaceMessageAttempt>(OnMessageAttempt);
        SubscribeLocalEvent<Wh40kAiOverwatchEyeComponent, MoveInputEvent>(OnEyeMoveInput);
    }

    public void LimitZoom(EntityUid watcher)
    {
        if (HasComp<Wh40kOverwatchZoomLimitComponent>(watcher)
            || !TryComp<ContentEyeComponent>(watcher, out var eye)
            || eye.MaxZoom is { X: <= 1f, Y: <= 1f })
            return;

        EnsureComp<Wh40kOverwatchZoomLimitComponent>(watcher).MaxZoom = eye.MaxZoom;
        _contentEye.SetMaxZoom(watcher, SharedContentEyeSystem.DefaultZoom, eye);
    }

    public EntityUid? ReleaseTarget(EntityUid watcher)
    {
        if (!TerminatingOrDeleted(watcher) && TryComp<Wh40kOverwatchZoomLimitComponent>(watcher, out var limit))
        {
            _contentEye.SetMaxZoom(watcher, limit.MaxZoom);
            RemComp(watcher, limit);
        }

        return RemoteEye(watcher);
    }

    private EntityUid? RemoteEye(EntityUid watcher)
    {
        if (TerminatingOrDeleted(watcher)
            || !HasComp<StationAiHeldComponent>(watcher)
            || !_stationAi.TryGetCore(watcher, out var core)
            || core.Comp?.RemoteEntity is not { } eye
            || TerminatingOrDeleted(eye))
            return null;

        return eye;
    }

    private void OnMessageAttempt(Entity<OverwatchConsoleComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        var (uid, component) = ent;
        if (args.Message is not OverwatchViewCameraMessage || RemoteEye(args.Actor) is not { } eye)
            return;

        EnsureComp<Wh40kAiOverwatchEyeComponent>(eye).Ai = args.Actor;
    }

    private void OnEyeMoveInput(Entity<Wh40kAiOverwatchEyeComponent> ent, ref MoveInputEvent args)
    {
        if (!args.HasDirectionalMovement)
            return;

        RemCompDeferred<Wh40kAiOverwatchEyeComponent>(ent);

        if (TryComp<RatOverwatchWatchingComponent>(ent.Comp.Ai, out var watching) && watching.Watching != null)
            RemComp<RatOverwatchWatchingComponent>(ent.Comp.Ai);
    }
}
