using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>
/// Drug levels, tiers and the speed they give. The server ticks levels and runs effects; the client draws overlays.
/// </summary>
public abstract class SharedWh40kIntoxicationSystem : EntitySystem
{
    [Dependency] protected IPrototypeManager Proto = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] protected MovementSpeedModifierSystem Movement = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    /// <summary>Speed while nodding: a drift, not a fall.</summary>
    public const float NodSpeed = 0.2f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kIntoxicationComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<Wh40kIntoxicationComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = GetSpeed(ent.Comp);
        if (IsNodding(ent.Comp))
            speed *= NodSpeed;

        if (!MathHelper.CloseTo(speed, 1f))
            args.ModifySpeed(speed, speed);
    }

    /// <summary>Whether the body is in an opioid nod: eyes shut, still standing.</summary>
    public bool IsNodding(Wh40kIntoxicationComponent comp)
    {
        return comp.NodEnd > _gameTiming.CurTime;
    }

    /// <summary>True when the body's brain is non-organic, so chemical doses don't register and neuroware loads.</summary>
    public bool IsNeurowareHost(EntityUid body)
    {
        if (HasComp<Wh40kNeurowareHostComponent>(body))
            return true;

        if (!TryComp<BodyComponent>(body, out var bodyComp))
            return false;

        foreach (var organ in _body.GetBodyOrgans(body, bodyComp))
        {
            if (HasComp<Wh40kNeurowareHostComponent>(organ.Id))
                return true;
        }

        return false;
    }

    /// <summary>How much a family's tolerance blunts its drugs in the blood: 1 with none, up to 2.</summary>
    public float ToleranceFactor(EntityUid uid, Wh40kDrugFamily family)
    {
        var ev = new Wh40kDrugDecayEvent(family);
        RaiseLocalEvent(uid, ref ev);
        return ev.Multiplier;
    }

    /// <summary>Raises a profile's fed level, as a neuroware program does.</summary>
    public void AddLevel(EntityUid uid, ProtoId<Wh40kDrugProfilePrototype> profile, float amount)
    {
        if (amount <= 0f || !Proto.TryIndex(profile, out _))
            return;

        var comp = EnsureComp<Wh40kIntoxicationComponent>(uid);
        var speed = GetSpeed(comp);
        comp.Fed[profile] = comp.Fed.GetValueOrDefault(profile) + amount;
        comp.Levels[profile] = comp.Levels.GetValueOrDefault(profile) + amount;
        Dirty(uid, comp);
        if (!MathHelper.CloseTo(GetSpeed(comp), speed))
            Movement.RefreshMovementSpeedModifiers(uid);
        RegisterDose(uid, profile, amount);
    }

    /// <summary>
    /// Counts a dose towards dependence without raising the level, for a drug whose level follows the blood. Starts
    /// tracking the body if it wasn't already, unless <paramref name="track"/> is false: a dose that gives no level
    /// has nothing to track.
    /// </summary>
    public void RegisterDose(EntityUid uid, ProtoId<Wh40kDrugProfilePrototype> profile, float amount, bool track = true)
    {
        if (amount <= 0f || !Proto.TryIndex(profile, out var proto))
            return;

        if (track)
            EnsureComp<Wh40kIntoxicationComponent>(uid);
        if (proto.DependenceGain > 0f)
            EnsureComp<Wh40kDependenceComponent>(uid);

        var ev = new Wh40kDrugDosedEvent(profile, proto.Family, amount, proto.DependenceGain * amount);
        RaiseLocalEvent(uid, ref ev);
    }

    /// <summary>Lowers the level of every profile in a family, as an antagonist does.</summary>
    public virtual void ReduceFamily(EntityUid uid, Wh40kDrugFamily family, float amount)
    {
        if (!TryComp<Wh40kIntoxicationComponent>(uid, out var comp))
            return;

        var speed = GetSpeed(comp);
        foreach (var profile in new List<ProtoId<Wh40kDrugProfilePrototype>>(comp.Levels.Keys))
        {
            if (!Proto.TryIndex(profile, out var proto) || proto.Family != family)
                continue;

            comp.Levels[profile] = Math.Max(0f, comp.Levels[profile] - amount);
            if (comp.Fed.TryGetValue(profile, out var fed))
                comp.Fed[profile] = Math.Max(0f, fed - amount);
            if (comp.Rush.TryGetValue(profile, out var rush))
                comp.Rush[profile] = Math.Max(0f, rush - amount);
        }

        Dirty(uid, comp);
        if (!MathHelper.CloseTo(GetSpeed(comp), speed))
            Movement.RefreshMovementSpeedModifiers(uid);
    }

    /// <summary>Drops every fed level to zero. Crashes still follow on the next tick, as they would when a high ends.</summary>
    public void ClearLevels(EntityUid uid)
    {
        if (!TryComp<Wh40kIntoxicationComponent>(uid, out var comp))
            return;

        var speed = GetSpeed(comp);
        foreach (var profile in new List<ProtoId<Wh40kDrugProfilePrototype>>(comp.Levels.Keys))
        {
            comp.Levels[profile] = 0f;
        }

        comp.Fed.Clear();
        comp.Rush.Clear();
        Dirty(uid, comp);
        if (!MathHelper.CloseTo(GetSpeed(comp), speed))
            Movement.RefreshMovementSpeedModifiers(uid);
    }

    public float GetLevel(EntityUid uid, ProtoId<Wh40kDrugProfilePrototype> profile)
    {
        return TryComp<Wh40kIntoxicationComponent>(uid, out var comp) ? comp.Levels.GetValueOrDefault(profile) : 0f;
    }

    /// <summary>The tier a profile is in, or -1 below its first.</summary>
    public int GetTier(Wh40kIntoxicationComponent comp, ProtoId<Wh40kDrugProfilePrototype> profile)
    {
        return comp.Levels.TryGetValue(profile, out var level) && Proto.TryIndex(profile, out var proto)
            ? proto.TierAt(level)
            : -1;
    }

    /// <summary>True while a profile is at or above its first tier. A crash waits while its profile is high.</summary>
    public bool IsHigh(Wh40kIntoxicationComponent comp, ProtoId<Wh40kDrugProfilePrototype> profile)
    {
        return GetTier(comp, profile) >= 0;
    }

    /// <summary>The crashes running now, leaving out those paused by a redose.</summary>
    public IEnumerable<ProtoId<Wh40kDrugProfilePrototype>> ActiveCrashes(Wh40kIntoxicationComponent comp)
    {
        foreach (var profile in comp.Crashes.Keys)
        {
            if (!IsHigh(comp, profile))
                yield return profile;
        }
    }

    /// <summary>True while any profile of a family is at or above its first tier.</summary>
    public bool IsFamilyHigh(EntityUid uid, Wh40kDrugFamily family)
    {
        if (!TryComp<Wh40kIntoxicationComponent>(uid, out var comp))
            return false;

        foreach (var (profile, level) in comp.Levels)
        {
            if (Proto.TryIndex(profile, out var proto) && proto.Family == family && proto.TierAt(level) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>The furthest phase any profile of a family is in.</summary>
    public Wh40kDrugPhase FamilyPhase(Wh40kIntoxicationComponent comp, Wh40kDrugFamily family)
    {
        var phase = Wh40kDrugPhase.None;
        foreach (var (profile, level) in comp.Levels)
        {
            if (!Proto.TryIndex(profile, out var proto) || proto.Family != family)
                continue;

            var reached = proto.PhaseOf(proto.TierAt(level));
            if (reached > phase)
                phase = reached;
        }

        return phase;
    }

    /// <summary>True while any active tier has the trait.</summary>
    public bool HasTrait(EntityUid uid, Wh40kDrugTrait trait)
    {
        if (!TryComp<Wh40kIntoxicationComponent>(uid, out var comp))
            return false;

        foreach (var (profile, level) in comp.Levels)
        {
            if (!Proto.TryIndex(profile, out var proto))
                continue;

            var tier = proto.TierAt(level);
            if (tier >= 0 && proto.Tiers[tier].Traits.Contains(trait))
                return true;
        }

        return false;
    }

    /// <summary>The product of every active tier's and crash's speed.</summary>
    public float GetSpeed(Wh40kIntoxicationComponent comp)
    {
        var speed = 1f;
        foreach (var (profile, level) in comp.Levels)
        {
            if (!Proto.TryIndex(profile, out var proto))
                continue;

            var tier = proto.TierAt(level);
            if (tier >= 0)
                speed *= proto.Tiers[tier].Speed;
        }

        foreach (var profile in comp.Crashes.Keys)
        {
            if (!IsHigh(comp, profile) && Proto.TryIndex(profile, out var proto) && proto.Crash != null)
                speed *= proto.Crash.Speed;
        }

        return speed;
    }
}

/// <summary>Raised on a body when a drug profile's level rises.</summary>
[ByRefEvent]
public readonly record struct Wh40kDrugDosedEvent(
    ProtoId<Wh40kDrugProfilePrototype> Profile,
    Wh40kDrugFamily Family,
    float Amount,
    float DependenceGain);

/// <summary>Raised on a body each tick per profile to scale how fast the level decays.</summary>
[ByRefEvent]
public record struct Wh40kDrugDecayEvent(Wh40kDrugFamily Family, float Multiplier = 1f);
