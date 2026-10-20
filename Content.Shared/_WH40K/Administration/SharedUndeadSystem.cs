using Content.Shared._Shitmed.Targeting;
using Content.Shared._White.Standing;
using Content.Shared.Actions.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Gibbing.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared.Standing;

namespace Content.Shared._WH40K.Administration;

public abstract partial class SharedUndeadSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UndeadComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<UndeadComponent, BodyPartAddedEvent>(OnPartAdded);
        SubscribeLocalEvent<UndeadComponent, BodyPartRemovedEvent>(OnPartRemoved);
        SubscribeLocalEvent<UndeadComponent, DamageChangeStartedEvent>(OnDamageStarted);
        SubscribeLocalEvent<UndeadComponent, DamageChangeFinishedEvent>(OnDamageFinished);
        SubscribeLocalEvent<UndeadComponent, DamageChangedEvent>(OnDamageChanged,
            before: new[] { typeof(MobThresholdSystem), typeof(SharedBodySystem), typeof(SharedDestructibleSystem) });
        SubscribeLocalEvent<UndeadPartComponent, DamageChangedEvent>(OnPartDamageChanged,
            before: new[] { typeof(MobThresholdSystem), typeof(SharedBodySystem), typeof(SharedDestructibleSystem) });
        SubscribeLocalEvent<UndeadComponent, MobStateChangingEvent>(OnMobStateChanging);
        SubscribeLocalEvent<UndeadComponent, GibbingAttemptEvent>(OnGibbingAttempt);
        SubscribeLocalEvent<UndeadPartComponent, GibbingAttemptEvent>(OnPartGibbingAttempt);
        SubscribeLocalEvent<UndeadComponent, DestructionAttemptEvent>(OnDestructionAttempt);
        SubscribeLocalEvent<UndeadComponent, DropHandItemsEvent>(OnDropHandItems,
            before: new[] { typeof(SharedHandsSystem) });
        SubscribeLocalEvent<UndeadComponent, DisarmAttemptEvent>(OnDisarmAttempt);
    }

    private void OnDropHandItems(Entity<UndeadComponent> ent, ref DropHandItemsEvent args)
    {
        args.Cancelled = true;
    }

    private void OnDisarmAttempt(EntityUid uid, UndeadComponent component, DisarmAttemptEvent args)
    {
        if (args.TargetItemInHandUid != null)
            args.Cancel();
    }

    public bool CanEnable(EntityUid uid)
    {
        return HasComp<DamageableComponent>(uid)
            && HasComp<MobThresholdsComponent>(uid)
            && _mobState.IsAlive(uid);
    }

    public bool ToggleUndead(EntityUid uid)
    {
        if (HasComp<UndeadComponent>(uid))
        {
            RemComp<UndeadComponent>(uid);
            return false;
        }

        EnsureComp<UndeadComponent>(uid);
        foreach (var (part, _) in _body.GetBodyChildren(uid))
            ProtectPart(uid, part);

        Rejuvenate(uid);
        return true;
    }

    private void Rejuvenate(EntityUid uid)
    {
        RaiseLocalEvent(uid, new RejuvenateEvent());

        // Removing knockdown can start an ordinary stand-up DoAfter. Cancel it before
        // standing instantly, or its damage-cancellation callback could put us back down.
        if (TryComp<DoAfterComponent>(uid, out var doAfter))
        {
            foreach (var (id, action) in doAfter.DoAfters)
            {
                if (action.Args.Event is StandingUpDoAfterEvent)
                    _doAfter.Cancel(uid, id, doAfter);
            }
        }

        _standing.Stand(uid, force: true);
    }

    private void ProtectPart(EntityUid body, EntityUid part)
    {
        var protection = EnsureComp<UndeadPartComponent>(part);
        protection.Body = body;
        Dirty(part, protection);
    }

    private void OnPartAdded(Entity<UndeadComponent> ent, ref BodyPartAddedEvent args)
    {
        ProtectPart(ent, args.Part);
    }

    private void OnPartRemoved(Entity<UndeadComponent> ent, ref BodyPartRemovedEvent args)
    {
        RemComp<UndeadPartComponent>(args.Part);
    }

    private void OnShutdown(Entity<UndeadComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        foreach (var (part, _) in _body.GetBodyChildren(ent))
            RemComp<UndeadPartComponent>(part);
    }

    private void OnDamageStarted(Entity<UndeadComponent> ent, ref DamageChangeStartedEvent args)
    {
        ent.Comp.DamageDepth++;
    }

    private void OnDamageFinished(Entity<UndeadComponent> ent, ref DamageChangeFinishedEvent args)
    {
        ent.Comp.DamageDepth--;
        if (ent.Comp.DamageDepth == 0 && ent.Comp.RecoveryPending)
            Recover(ent);
    }

    private void OnDamageChanged(Entity<UndeadComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.Recovering)
            return;

        var received = args.DamageDelta == null || args.DamageDelta.Empty
            ? FixedPoint2.Max(0, args.Damageable.TotalDamage - ent.Comp.LastDamage)
            : PositiveDamage(args.DamageDelta);
        ent.Comp.TotalDamage += received;
        ent.Comp.LastDamage = args.Damageable.TotalDamage;
        Dirty(ent);

        if (!TryComp<MobThresholdsComponent>(ent, out var thresholds))
            return;

        foreach (var (threshold, state) in thresholds.Thresholds)
        {
            if (state is MobState.Critical or MobState.Dead && args.Damageable.TotalDamage >= threshold)
            {
                RequestRecovery(ent);
                return;
            }
        }

        if (ent.Comp.RecoveryPending)
            RequestRecovery(ent);
    }

    private void OnPartDamageChanged(Entity<UndeadPartComponent> ent, ref DamageChangedEvent args)
    {
        var body = ent.Comp.Body;
        if (!TryComp<UndeadComponent>(body, out var undead)
            || !TryComp<BodyPartComponent>(ent, out var part) || part.Body != body
            || undead.Recovering
            || args.Damageable.TotalDamage < part.IntegrityThresholds[TargetIntegrity.CriticallyWounded])
        {
            return;
        }

        if (undead.DamageDepth == 0)
        {
            var received = args.DamageDelta == null ? args.Damageable.TotalDamage : PositiveDamage(args.DamageDelta);
            undead.TotalDamage += received;
            Recover((body, undead));
            return;
        }

        undead.RecoveryPending = true;
        // Keep the part functional until the full hit is known. This runs before the
        // body system can disable a hand, drop equipment, sever a part or kill its owner.
        _damage.SetAllDamage(ent, args.Damageable, 0);
    }

    private void OnMobStateChanging(Entity<UndeadComponent> ent, ref MobStateChangingEvent args)
    {
        if (args.NewState is not (MobState.Critical or MobState.Dead))
            return;

        args.Cancel();
        RequestRecovery(ent);
    }

    private void OnGibbingAttempt(Entity<UndeadComponent> ent, ref GibbingAttemptEvent args)
    {
        args.Cancel();
        RequestRecovery(ent);
    }

    private void OnDestructionAttempt(Entity<UndeadComponent> ent, ref DestructionAttemptEvent args)
    {
        args.Cancel();
        RequestRecovery(ent);
    }

    private void OnPartGibbingAttempt(Entity<UndeadPartComponent> ent, ref GibbingAttemptEvent args)
    {
        var body = ent.Comp.Body;
        if (!TryComp<UndeadComponent>(body, out var undead)
            || !TryComp<BodyPartComponent>(ent, out var part) || part.Body != body)
            return;

        args.Cancel();
        if (undead.DamageDepth > 0)
            undead.RecoveryPending = true;
        else
            RequestRecovery((body, undead));
    }

    private void RequestRecovery(Entity<UndeadComponent> ent)
    {
        if (ent.Comp.Recovering)
            return;

        if (ent.Comp.DamageDepth == 0)
        {
            Recover(ent);
            return;
        }

        ent.Comp.RecoveryPending = true;
        ent.Comp.Recovering = true;
        try
        {
            if (TryComp<DamageableComponent>(ent, out var damageable))
                _damage.SetAllDamage(ent, damageable, 0);
        }
        finally
        {
            ent.Comp.Recovering = false;
        }
    }

    private void Recover(Entity<UndeadComponent> ent)
    {
        if (ent.Comp.Recovering || TerminatingOrDeleted(ent))
            return;

        ent.Comp.Recovering = true;
        try
        {
            ent.Comp.Deaths++;
            ent.Comp.RecoveryPending = false;
            Rejuvenate(ent);
            ent.Comp.LastDamage = 0;
            Dirty(ent);
            OnRecovered(ent);
        }
        finally
        {
            ent.Comp.Recovering = false;
        }
    }

    protected virtual void OnRecovered(Entity<UndeadComponent> ent)
    {
    }

    private static FixedPoint2 PositiveDamage(DamageSpecifier damage)
    {
        var total = FixedPoint2.Zero;
        foreach (var value in damage.DamageDict.Values)
            total += FixedPoint2.Max(0, value);
        return total;
    }
}
