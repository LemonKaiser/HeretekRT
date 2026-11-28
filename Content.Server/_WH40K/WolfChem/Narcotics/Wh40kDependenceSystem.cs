using Content.Server.Medical;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// Round-long dependence: doses fill a meter per family, tolerance speeds up the come-down, and a dependent body that
/// goes without for long enough falls into withdrawal until it is high on the family again. Withdrawal never kills.
/// </summary>
public sealed partial class Wh40kDependenceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private VomitSystem _vomit = default!;
    [Dependency] private Wh40kIntoxicationSystem _intoxication = default!;

    /// <summary>Meter and tolerance lost a second while clean: 0.02 a minute.</summary>
    public const float DecayPerSecond = 0.02f / 60f;

    /// <summary>Share of that decay a meter above <see cref="WithdrawalMeter"/> keeps: a habit doesn't wait out.</summary>
    public const float DependentDecay = 0.25f;

    /// <summary>Tolerance gained per point of dependence gained.</summary>
    public const float ToleranceRatio = 0.5f;

    /// <summary>Withdrawal needs a meter above this.</summary>
    public const float WithdrawalMeter = 0.5f;

    /// <summary>Seconds below the family's first tier before withdrawal starts.</summary>
    public const float WithdrawalDelay = 300f;

    /// <summary>Stamina damage withdrawal keeps a body at, as a share of its crit threshold.</summary>
    public const float StaminaFloor = 0.35f;

    /// <summary>Stamina damage a second a dependent body sheds while its family is high.</summary>
    public const float SatisfiedStamina = 1f;

    private const int CravingLines = 3;
    private const float VomitChance = 1f / 90f;
    private static readonly TimeSpan StatusTime = TimeSpan.FromSeconds(2.5);

    private TimeSpan _nextTick;
    private readonly List<Wh40kDrugFamily> _families = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kDependenceComponent, Wh40kDrugDosedEvent>(OnDosed);
        SubscribeLocalEvent<Wh40kDependenceComponent, Wh40kDrugDecayEvent>(OnDecay);
        SubscribeLocalEvent<Wh40kDependentStimulantComponent, ComponentStartup>((uid, comp, _) => ApplyTrait(uid, comp));
        SubscribeLocalEvent<Wh40kDependentOpioidComponent, ComponentStartup>((uid, comp, _) => ApplyTrait(uid, comp));
        SubscribeLocalEvent<Wh40kDependentNicotineComponent, ComponentStartup>((uid, comp, _) => ApplyTrait(uid, comp));
        SubscribeLocalEvent<Wh40kDependentEmpathogenComponent, ComponentStartup>((uid, comp, _) => ApplyTrait(uid, comp));
        SubscribeLocalEvent<Wh40kDependentCannabinoidComponent, ComponentStartup>((uid, comp, _) => ApplyTrait(uid, comp));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(1);
        Tick(1f);
    }

    private void ApplyTrait(EntityUid uid, Wh40kDependentTraitComponent trait)
    {
        var dep = EnsureComp<Wh40kDependenceComponent>(uid);
        dep.Meter[trait.Family] = Math.Max(dep.Meter.GetValueOrDefault(trait.Family), trait.Start);
        dep.Floor[trait.Family] = trait.Floor;
        dep.SinceUse.TryAdd(trait.Family, 0f);

        var gear = _intoxication.IsNeurowareHost(uid) ? trait.HostGear : trait.Gear;
        if (gear == null)
            return;

        var item = SpawnAttachedTo(gear, Transform(uid).Coordinates);
        _hands.TryPickupAnyHand(uid, item, checkActionBlocker: false);
    }

    private void OnDosed(Entity<Wh40kDependenceComponent> ent, ref Wh40kDrugDosedEvent args)
    {
        var dep = ent.Comp;
        var family = args.Family;
        dep.Meter[family] = Math.Clamp(dep.Meter.GetValueOrDefault(family) + args.DependenceGain, 0f, 1f);
        dep.Tolerance[family] = Math.Clamp(dep.Tolerance.GetValueOrDefault(family) + args.DependenceGain * ToleranceRatio, 0f, 1f);
    }

    private void OnDecay(Entity<Wh40kDependenceComponent> ent, ref Wh40kDrugDecayEvent args)
    {
        args.Multiplier *= 1f + ent.Comp.Tolerance.GetValueOrDefault(args.Family);
    }

    /// <summary>The withdrawal stage a meter gives once withdrawal has started. Nicotine and cannabis stop at cravings.</summary>
    public static int StageFor(Wh40kDrugFamily family, float meter)
    {
        var stage = meter >= 0.85f ? 3 : meter >= 0.7f ? 2 : meter > WithdrawalMeter ? 1 : 0;
        return family is Wh40kDrugFamily.Nicotine or Wh40kDrugFamily.Cannabinoid ? Math.Min(stage, 1) : stage;
    }

    /// <summary>Advances every dependent body by <paramref name="seconds"/>.</summary>
    public void Tick(float seconds)
    {
        var query = EntityQueryEnumerator<Wh40kDependenceComponent>();
        while (query.MoveNext(out var uid, out var dep))
        {
            if (_mobState.IsDead(uid))
                continue;

            _families.Clear();
            _families.AddRange(dep.Meter.Keys);
            foreach (var family in _families)
            {
                TickFamily((uid, dep), family, seconds);
            }

            if (dep.Meter.Count == 0)
                RemCompDeferred<Wh40kDependenceComponent>(uid);
        }
    }

    private void TickFamily(Entity<Wh40kDependenceComponent> ent, Wh40kDrugFamily family, float seconds)
    {
        var dep = ent.Comp;
        var high = _intoxication.IsFamilyHigh(ent, family);
        var since = high ? 0f : dep.SinceUse.GetValueOrDefault(family) + seconds;
        dep.SinceUse[family] = since;

        var floor = dep.Floor.GetValueOrDefault(family);
        var meter = dep.Meter[family];
        if (!high)
        {
            var decay = DecayPerSecond * seconds;
            if (meter > WithdrawalMeter)
                decay = Math.Min(decay * DependentDecay, meter - WithdrawalMeter);
            meter = Math.Max(floor, meter - decay);
            dep.Tolerance[family] = Math.Max(0f, dep.Tolerance.GetValueOrDefault(family) - DecayPerSecond * seconds);
        }

        dep.Meter[family] = meter;
        Satisfy(ent, family, high && meter > WithdrawalMeter, seconds);

        var stage = since >= WithdrawalDelay ? StageFor(family, meter) : 0;
        if (stage != dep.Stage.GetValueOrDefault(family))
            SetStage(ent, dep, family, stage);

        if (stage > 0)
            Withdraw(ent, dep, family, stage, seconds);

        if (meter <= 0f && floor <= 0f && dep.Tolerance.GetValueOrDefault(family) <= 0f)
        {
            dep.Meter.Remove(family);
            dep.Tolerance.Remove(family);
            dep.SinceUse.Remove(family);
            dep.Stage.Remove(family);
            dep.NextCraving.Remove(family);
            dep.Satisfied.Remove(family);
        }
    }

    private void Satisfy(Entity<Wh40kDependenceComponent> ent, Wh40kDrugFamily family, bool satisfied, float seconds)
    {
        if (!satisfied)
        {
            ent.Comp.Satisfied.Remove(family);
            return;
        }

        if (ent.Comp.Satisfied.Add(family) && ent.Comp.Stage.GetValueOrDefault(family) == 0)
        {
            _popup.PopupEntity(Loc.GetString(Prefix(ent) + "satisfied", ("family", FamilyName(family))), ent, ent,
                PopupType.Medium);
        }

        if (TryComp<StaminaComponent>(ent, out var stamina) && !stamina.Critical && stamina.StaminaDamage > 0f)
            _stamina.TakeStaminaDamage(ent, -SatisfiedStamina * seconds, stamina, visual: false);
    }

    private void SetStage(EntityUid uid, Wh40kDependenceComponent dep, Wh40kDrugFamily family, int stage)
    {
        var old = dep.Stage.GetValueOrDefault(family);
        dep.Stage[family] = stage;
        dep.NextCraving[family] = _random.NextFloat(20f, 40f);

        var suffix = stage == 0 ? "relief" : stage > old ? $"stage-{stage}" : null;
        if (suffix != null)
        {
            _popup.PopupEntity(Loc.GetString(Prefix(uid) + suffix, ("family", FamilyName(family))), uid, uid,
                stage == 0 ? PopupType.Medium : PopupType.MediumCaution);
        }
    }

    private void Withdraw(EntityUid uid, Wh40kDependenceComponent dep, Wh40kDrugFamily family, int stage, float seconds)
    {
        var craving = dep.NextCraving.GetValueOrDefault(family) - seconds;
        if (craving <= 0f)
        {
            craving = _random.NextFloat(45f, 90f);
            var line = $"{Prefix(uid)}craving-{family.ToString().ToLowerInvariant()}-{_random.Next(1, CravingLines + 1)}";
            _popup.PopupEntity(Loc.GetString(line), uid, uid, PopupType.SmallCaution);
        }

        dep.NextCraving[family] = craving;

        if (stage < 2)
            return;

        _jitter.DoJitter(uid, StatusTime, true, 4f, 3f);

        if (TryComp<StaminaComponent>(uid, out var stamina))
        {
            var floor = stamina.CritThreshold * StaminaFloor;
            if (stamina.StaminaDamage < floor)
                _stamina.TakeStaminaDamage(uid, floor - stamina.StaminaDamage, stamina, visual: false);
        }

        if (stage < 3 || family is not (Wh40kDrugFamily.Opioid or Wh40kDrugFamily.Stimulant))
            return;

        if (_status.TryAddStatusEffect(uid, "Drowsiness", StatusTime, true, "Drowsiness"))
            _intoxication.TameDrowsiness(uid);

        if (!_intoxication.IsNeurowareHost(uid) && _random.Prob(Math.Min(1f, VomitChance * seconds)))
            _vomit.Vomit(uid, -10f, -10f);
    }

    /// <summary>Whether withdrawal from a family at a stage keeps a status effect going.</summary>
    public static bool WithdrawalHolds(Wh40kDrugFamily family, int stage, string key)
    {
        return key == "Jitter" && stage >= 2
            || key == "Drowsiness" && stage >= 3 && family is Wh40kDrugFamily.Opioid or Wh40kDrugFamily.Stimulant;
    }

    /// <summary>The health analyzer's view of one family's dependence.</summary>
    public static Wh40kAnalyzerHabit Habit(Wh40kDependenceComponent dep, Wh40kDrugFamily family)
    {
        var meter = dep.Meter.GetValueOrDefault(family);
        var floor = dep.Floor.GetValueOrDefault(family);
        var stage = dep.Stage.GetValueOrDefault(family);
        var withdrawalIn = stage == 0 && StageFor(family, meter) > 0
            ? Math.Max(0f, WithdrawalDelay - dep.SinceUse.GetValueOrDefault(family))
            : -1f;
        var cleanIn = meter <= WithdrawalMeter ? -1f
            : floor > WithdrawalMeter ? float.PositiveInfinity
            : (meter - WithdrawalMeter) / (DecayPerSecond * DependentDecay);

        return new Wh40kAnalyzerHabit(family, meter, floor, dep.Tolerance.GetValueOrDefault(family), stage, withdrawalIn,
            cleanIn);
    }

    private string Prefix(EntityUid uid)
    {
        return _intoxication.IsNeurowareHost(uid) ? "wh40k-withdrawal-host-" : "wh40k-withdrawal-";
    }

    private string FamilyName(Wh40kDrugFamily family)
    {
        return Loc.GetString($"wh40k-drug-family-{family.ToString().ToLowerInvariant()}");
    }
}
