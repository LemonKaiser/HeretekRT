using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Synthetics;

/// <summary>
/// Isolates or restores cybernetic chassis limbs from the chassis panel (and tracks low-power arm cuts
/// separately so user-isolated arms stay isolated when power returns).
/// </summary>
public sealed partial class Wh40kChassisIsolationSystem : EntitySystem
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    private TimeSpan _nextSweep;

    public static bool CanIsolate(BodyPartComponent part) =>
        part.PartType is BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot;

    public bool TryIsolate(EntityUid part)
    {
        if (!TryComp<BodyPartComponent>(part, out var bodyPart) ||
            bodyPart.Body is not { } body ||
            !CanIsolate(bodyPart) ||
            HasComp<Wh40kIsolatedPartComponent>(part))
        {
            return false;
        }

        var isolated = EnsureComp<Wh40kIsolatedPartComponent>(part);
        isolated.CouldEnable = bodyPart.CanEnable;
        Dirty(part, isolated);

        bodyPart.CanEnable = false;
        var ev = new BodyPartEnableChangedEvent(false);
        RaiseLocalEvent(part, ref ev);
        Dirty(part, bodyPart);

        _speed.RefreshMovementSpeedModifiers(body);
        return true;
    }

    public bool TryRestore(EntityUid part)
    {
        if (!TryComp<Wh40kIsolatedPartComponent>(part, out var isolated))
            return false;

        var couldEnable = isolated.CouldEnable;
        RemComp<Wh40kIsolatedPartComponent>(part);
        RemComp<Wh40kLowPowerPartComponent>(part);

        if (!TryComp<BodyPartComponent>(part, out var bodyPart))
            return true;

        bodyPart.CanEnable = couldEnable;
        var functional = !TryComp<DamageableComponent>(part, out var dmg) ||
                         dmg.TotalDamage.Float() < bodyPart.SeverIntegrity;
        if (couldEnable && functional)
        {
            var ev = new BodyPartEnableChangedEvent(true);
            RaiseLocalEvent(part, ref ev);
        }

        Dirty(part, bodyPart);

        if (bodyPart.Body is { } body)
            _speed.RefreshMovementSpeedModifiers(body);

        return true;
    }

    public bool IsUserIsolated(EntityUid part) =>
        HasComp<Wh40kIsolatedPartComponent>(part) &&
        (!TryComp<Wh40kLowPowerPartComponent>(part, out var lowPower) || lowPower.UserIsolated);

    public bool TrySetUserIsolated(EntityUid part, bool isolate, out bool deferred)
    {
        deferred = false;
        if (!TryComp<Wh40kLowPowerPartComponent>(part, out var lowPower))
            return isolate ? TryIsolate(part) : TryRestore(part);

        if (lowPower.UserIsolated == isolate)
            return false;

        lowPower.UserIsolated = isolate;
        deferred = true;
        return true;
    }

    public bool TryCutPower(EntityUid part)
    {
        if (HasComp<Wh40kLowPowerPartComponent>(part))
            return false;

        var user = HasComp<Wh40kIsolatedPartComponent>(part);
        if (!user && !TryIsolate(part))
            return false;

        EnsureComp<Wh40kLowPowerPartComponent>(part).UserIsolated = user;
        return true;
    }

    public bool TryRestorePower(EntityUid part)
    {
        if (!TryComp<Wh40kLowPowerPartComponent>(part, out var lowPower))
            return false;

        var user = lowPower.UserIsolated;
        RemComp<Wh40kLowPowerPartComponent>(part);
        if (!user)
            TryRestore(part);

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;
        var stale = new List<EntityUid>();
        var query = EntityQueryEnumerator<Wh40kIsolatedPartComponent, BodyPartComponent>();
        while (query.MoveNext(out var uid, out _, out var part))
        {
            if (part.Body == null)
                stale.Add(uid);
        }

        foreach (var uid in stale)
            TryRestore(uid);
    }
}

[RegisterComponent]
public sealed partial class Wh40kLowPowerPartComponent : Component
{
    [DataField]
    public bool UserIsolated;
}
