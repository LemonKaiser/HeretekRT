using System.Numerics;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Drowsiness;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// Ticks drug levels once a second: reads the blood, decays fed level, applies the tier each reaches, and starts and
/// ends crashes.
/// </summary>
public sealed partial class Wh40kIntoxicationSystem : SharedWh40kIntoxicationSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    /// <summary>Seconds between ticks.</summary>
    public const float TickSeconds = 1f;

    /// <summary>How long a tier's status effects outlast the tick that set them.</summary>
    private static readonly TimeSpan StatusTime = TimeSpan.FromSeconds(TickSeconds * 2.5f);

    /// <summary>Seconds between the nods of a crash or withdrawal Drowsiness, (min, max).</summary>
    public static readonly Vector2 DrowsyGap = new(60f, 120f);

    /// <summary>Seconds each nod puts the body to sleep, (min, max).</summary>
    public static readonly Vector2 DrowsyLength = new(1f, 2f);

    private const int HallucinationVariants = 3;

    private TimeSpan _nextTick;
    private readonly List<ProtoId<Wh40kDrugProfilePrototype>> _profiles = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(TickSeconds);
        Tick(TickSeconds);
    }

    /// <summary>Advances every intoxicated body by <paramref name="seconds"/>.</summary>
    public void Tick(float seconds)
    {
        var query = EntityQueryEnumerator<Wh40kIntoxicationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            TickBody((uid, comp), seconds);
        }
    }

    private void TickBody(Entity<Wh40kIntoxicationComponent> ent, float seconds)
    {
        var (uid, comp) = ent;
        var speedBefore = GetSpeed(comp);
        var args = new EntityEffectBaseArgs(uid, EntityManager);
        var alive = !_mobState.IsDead(uid);

        var blood = ReadBlood(uid);
        _profiles.Clear();
        _profiles.AddRange(comp.Levels.Keys);
        foreach (var profile in _blood.Keys)
        {
            if (!comp.Levels.ContainsKey(profile))
                _profiles.Add(profile);
        }

        foreach (var profile in _profiles)
        {
            if (!Proto.TryIndex(profile, out var proto))
            {
                comp.Levels.Remove(profile);
                comp.Fed.Remove(profile);
                comp.Rush.Remove(profile);
                comp.BloodSeen.Remove(profile);
                continue;
            }

            var tolerance = ToleranceFactor(uid, proto.Family);
            var fed = Decayed(proto, comp.Fed.GetValueOrDefault(profile), tolerance * seconds);
            var fromBlood = _blood.GetValueOrDefault(profile) / tolerance;
            var level = fed + fromBlood;
            var rush = Rush(comp, profile, proto, fromBlood, seconds);
            if (rush > 0f)
            {
                var peak = level + rush;
                level = proto.Tiers.Count > 1 ? Math.Max(level, Math.Min(peak, proto.Tiers[^1].Min - 0.01f)) : peak;
            }

            var tier = proto.TierAt(level);

            if (fed > 0f)
                comp.Fed[profile] = fed;
            else
                comp.Fed.Remove(profile);

            if (blood is { } soln)
                Flush(soln, profile, proto, level, fromBlood, seconds);
            AnnouncePhase(ent, profile, proto, tier);

            if (tier >= 0)
            {
                comp.TimeAbove[profile] = comp.TimeAbove.GetValueOrDefault(profile) + seconds;
                if (alive)
                {
                    Apply(uid, args, proto.Tiers[tier].Effects, proto.Tiers[tier].StatusEffects);
                    Hallucinate(uid, proto.Tiers[tier]);
                }
            }
            else if (comp.TimeAbove.Remove(profile, out var above) && proto.Crash is { } crash && above >= crash.After)
            {
                if (alive && !comp.Crashes.ContainsKey(profile))
                    _stun.TryKnockdown(uid, crash.Collapse, true);

                var duration = comp.Crashes.GetValueOrDefault(profile) + crash.Duration.TotalSeconds + crash.PerSecondAbove * above;
                comp.Crashes[profile] = (float) Math.Min(duration, crash.MaxDuration.TotalSeconds);
            }

            if (level <= 0f)
                comp.Levels.Remove(profile);
            else
                comp.Levels[profile] = level;
        }

        _profiles.Clear();
        _profiles.AddRange(comp.Crashes.Keys);
        foreach (var profile in _profiles)
        {
            if (IsHigh(comp, profile))
                continue;

            var left = comp.Crashes[profile] - seconds;
            if (left <= 0f || !Proto.TryIndex(profile, out var proto) || proto.Crash == null)
            {
                comp.Crashes.Remove(profile);
                continue;
            }

            comp.Crashes[profile] = left;
            if (!alive)
                continue;

            Apply(uid, args, proto.Crash.Effects, proto.Crash.StatusEffects);
            Drain(uid, proto.Crash, seconds);
            if (proto.Crash.StatusEffects.Exists(s => s.Key == SharedDrowsinessSystem.DrowsinessKey))
                TameDrowsiness(uid);
        }

        UpdateTraits(ent, seconds, alive);

        if (!MathHelper.CloseTo(GetSpeed(comp), speedBefore))
            Movement.RefreshMovementSpeedModifiers(uid);

        if (comp.Levels.Count == 0 && comp.Crashes.Count == 0 && comp.Debt <= 0f)
        {
            RemCompDeferred<Wh40kIntoxicationComponent>(uid);
            return;
        }

        Dirty(uid, comp);
    }

    /// <summary>The fed level after <paramref name="seconds"/> of decay.</summary>
    public static float Decayed(Wh40kDrugProfilePrototype proto, float level, float seconds)
    {
        return Math.Max(0f, level - proto.Decay * seconds);
    }

    /// <summary>Gives a fresh dose's rush for level fed without the blood, as a neuroware program feeds it.</summary>
    public void AddRush(EntityUid uid, ProtoId<Wh40kDrugProfilePrototype> profile, float level)
    {
        if (level <= 0f
            || !Proto.TryIndex(profile, out var proto)
            || proto.RushGain <= 0f
            || !TryComp<Wh40kIntoxicationComponent>(uid, out var comp))
            return;

        comp.Rush[profile] = comp.Rush.GetValueOrDefault(profile) + level * proto.RushGain;
    }

    private static float Rush(Wh40kIntoxicationComponent comp, ProtoId<Wh40kDrugProfilePrototype> profile,
        Wh40kDrugProfilePrototype proto, float fromBlood, float seconds)
    {
        var rush = comp.Rush.GetValueOrDefault(profile);
        if (proto.RushHalfLife > 0f)
            rush *= MathF.Pow(2f, -seconds / proto.RushHalfLife);

        var rise = fromBlood - comp.BloodSeen.GetValueOrDefault(profile);
        if (rise > 0f)
            rush += rise * proto.RushGain;

        if (fromBlood > 0f)
            comp.BloodSeen[profile] = fromBlood;
        else
            comp.BloodSeen.Remove(profile);

        if (rush < 0.05f)
        {
            comp.Rush.Remove(profile);
            return 0f;
        }

        comp.Rush[profile] = rush;
        return rush;
    }

    private void AnnouncePhase(Entity<Wh40kIntoxicationComponent> ent, ProtoId<Wh40kDrugProfilePrototype> profile,
        Wh40kDrugProfilePrototype proto, int tier)
    {
        var phase = proto.PhaseOf(tier);
        var before = ent.Comp.Phases.GetValueOrDefault(profile, Wh40kDrugPhase.None);
        if (phase == before)
            return;

        if (phase == Wh40kDrugPhase.None)
            ent.Comp.Phases.Remove(profile);
        else
            ent.Comp.Phases[profile] = phase;

        var line = (phase, phase > before) switch
        {
            (Wh40kDrugPhase.Light, true) => "wh40k-drug-phase-light",
            (Wh40kDrugPhase.Heavy, true) => "wh40k-drug-phase-heavy",
            (Wh40kDrugPhase.Overdose, true) => "wh40k-drug-phase-overdose",
            (Wh40kDrugPhase.Heavy, false) => "wh40k-drug-phase-easing",
            (Wh40kDrugPhase.Light, false) => "wh40k-drug-phase-fading",
            _ => null,
        };

        if (line == null)
            return;

        var type = phase == Wh40kDrugPhase.Overdose ? PopupType.LargeCaution
            : phase > before ? PopupType.Medium
            : PopupType.Small;
        _popup.PopupEntity(Loc.GetString(line), ent, ent, type);
    }

    private void Apply(EntityUid uid, EntityEffectBaseArgs args, List<EntityEffect> effects, List<Wh40kDrugStatusEffect> statuses)
    {
        foreach (var effect in effects)
        {
            if (effect.ShouldApply(args, _random))
                effect.Effect(args);
        }

        foreach (var status in statuses)
        {
            if (status.Component != null)
                _status.TryAddStatusEffect(uid, status.Key, StatusTime, true, status.Component);
            else
                _status.TryAddStatusEffect(uid, status.Key, StatusTime, true);
        }
    }

    private void Drain(EntityUid uid, Wh40kDrugCrash crash, float seconds)
    {
        if (crash.Stamina <= 0f || !TryComp<StaminaComponent>(uid, out var stamina) || stamina.Critical)
            return;

        var room = stamina.CritThreshold * crash.StaminaCap - stamina.StaminaDamage;
        if (room > 0f)
            _stamina.TakeStaminaDamage(uid, Math.Min(crash.Stamina * seconds, room), stamina, visual: false, immediate: false);
    }

    public void TameDrowsiness(EntityUid uid)
    {
        if (!TryComp<DrowsinessComponent>(uid, out var drowsy) || drowsy.TimeBetweenIncidents == DrowsyGap)
            return;

        drowsy.TimeBetweenIncidents = DrowsyGap;
        drowsy.DurationOfIncident = DrowsyLength;
        drowsy.NextIncidentTime = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(DrowsyGap.X, DrowsyGap.Y));
    }

    private void Hallucinate(EntityUid uid, Wh40kDrugTier tier)
    {
        if (tier.Hallucinations.Count == 0 || !_random.Prob(tier.HallucinationChance))
            return;

        var kind = _random.Pick(tier.Hallucinations).ToString().ToLowerInvariant();
        var variant = _random.Next(1, HallucinationVariants + 1);
        var key = $"wh40k-drug-hallucination-{kind}-{variant}";
        var type = tier.Overlay >= 0.85f ? PopupType.MediumCaution : PopupType.Small;
        _popup.PopupEntity(Loc.GetString(key), uid, uid, type);
    }
}
