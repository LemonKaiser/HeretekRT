using Content.Server._WH40K.WolfChem.Narcotics;
using Content.Server.Chat.Systems;
using Content.Server.Nutrition.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Server.Stunnable;
using Content.Server.Temperature.Systems;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WH40K.Synthetics;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared._WH40K.WolfChem.Neuroware;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.WolfChem.Neuroware;

/// <summary>
/// Neuroware: data sticks load drug programs into a cybernetic/synthetic host, programs feed the shared intoxication
/// levels at the stick's integrity, damaged builds glitch, and the cybernetic HUD shows running program faults.
/// </summary>
public sealed partial class Wh40kNeurowareSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private Wh40kNarcoticGoodsSystem _goods = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private StunSystem _stun = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private Wh40kIntoxicationSystem _intox = default!;

    public const float CopyIntegrity = 0.85f;
    public const float CopyWear = 0.9f;
    public const int EncodeLoads = 3;
    public const float EncodeMinimum = 1f;
    public const float GlitchChance = 0.15f;
    public const float OverdriveDrain = 0.002f;
    public const float OverdriveHeat = 1500f;

    private const string AdviceLine = "wh40k-neuroware-advice";
    private const string CrashLine = "wh40k-neuroware-crash-fault";
    private const string HabitLine = "wh40k-neuroware-habit-loop";
    private const int FalseFaults = 3;
    private static readonly TimeSpan FalseFaultTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScrambleTime = TimeSpan.FromSeconds(10);
    private static readonly string[] GlitchEmotes = { "Beep", "Buzz" };

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kNeurowareStickComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<Wh40kNeurowareStickComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<Wh40kNeurowareStickComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<Wh40kNeurowareStickComponent, Wh40kNeurowareLoadDoAfterEvent>(OnLoadDoAfter);
        SubscribeLocalEvent<Wh40kNeurowareStickComponent, Wh40kNeurowareEncodeDoAfterEvent>(OnEncodeDoAfter);
        SubscribeLocalEvent<Wh40kCyberneticHudComponent, Wh40kCyberneticHudCollectEvent>(OnCollectFaults);
        SubscribeLocalEvent<BodyComponent, Wh40kChassisRebootedEvent>(OnRebooted);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(1);
        Tick(1f);
    }

    private void OnExamined(Entity<Wh40kNeurowareStickComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Profile is not { } profile || !_proto.TryIndex(profile, out var proto) || proto.Neuroware == null)
        {
            args.PushMarkup(Loc.GetString("wh40k-neuroware-stick-blank"));
            return;
        }

        args.PushMarkup(Loc.GetString("wh40k-neuroware-stick-examine",
            ("program", Loc.GetString(proto.Neuroware.Program)),
            ("integrity", (int) MathF.Round(ent.Comp.Integrity * 100f)),
            ("loads", ent.Comp.Loads)));
    }

    private void OnAfterInteract(Entity<Wh40kNeurowareStickComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (TryComp<Wh40kNeurowareStickComponent>(target, out var blank))
        {
            args.Handled = true;
            TryCopy(ent, (target, blank), args.User);
            return;
        }

        if (!HasComp<BodyComponent>(target))
        {
            if (ent.Comp.Profile == null)
                args.Handled = TryStartEncode(ent, args.User, target);
            return;
        }

        args.Handled = true;
        TryStartLoad(ent, args.User, target);
    }

    private void OnUseInHand(Entity<Wh40kNeurowareStickComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryStartLoad(ent, args.User, args.User);
    }

    public bool TryStartLoad(Entity<Wh40kNeurowareStickComponent> stick, EntityUid user, EntityUid target)
    {
        if (!CanLoad(stick, user, target))
            return false;

        var self = user == target;
        var args = new DoAfterArgs(EntityManager, user, self ? stick.Comp.SelfDelay : stick.Comp.OtherDelay,
            new Wh40kNeurowareLoadDoAfterEvent(), stick, target: target, used: stick)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return false;

        if (!self)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-neuroware-load-target", ("user", Identity.Entity(user, EntityManager))),
                target, target, PopupType.MediumCaution);
        }

        return true;
    }

    private bool CanLoad(Entity<Wh40kNeurowareStickComponent> stick, EntityUid user, EntityUid target)
    {
        if (stick.Comp.Profile == null)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-neuroware-stick-blank"), stick, user);
            return false;
        }

        if (stick.Comp.Loads <= 0)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-neuroware-stick-spent"), stick, user);
            return false;
        }

        if (!_intox.IsNeurowareHost(target))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-neuroware-no-port", ("target", Identity.Entity(target, EntityManager))),
                target, user);
            return false;
        }

        return true;
    }

    private void OnLoadDoAfter(Entity<Wh40kNeurowareStickComponent> ent, ref Wh40kNeurowareLoadDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = Load(ent, args.User, target);
    }

    public bool Load(Entity<Wh40kNeurowareStickComponent> stick, EntityUid user, EntityUid target)
    {
        if (!CanLoad(stick, user, target)
            || stick.Comp.Profile is not { } profile
            || !_proto.TryIndex(profile, out var proto)
            || proto.Neuroware is not { } twin)
            return false;

        stick.Comp.Loads--;
        var running = EnsureComp<Wh40kNeurowareComponent>(target);
        running.Programs.Add(new Wh40kNeurowareProgram
        {
            Profile = profile,
            Remaining = stick.Comp.Amount ?? twin.Amount,
            Rate = stick.Comp.Rate,
            Integrity = stick.Comp.Integrity,
        });

        if (twin.Banner is { } banner)
            _popup.PopupEntity(Loc.GetString(banner), target, target, PopupType.Medium);

        return true;
    }

    public bool TryCopy(Entity<Wh40kNeurowareStickComponent> source, Entity<Wh40kNeurowareStickComponent> blank, EntityUid? user)
    {
        if (source.Comp.Profile == null || blank.Comp.Profile != null)
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("wh40k-neuroware-copy-fail"), blank, user.Value);
            return false;
        }

        if (source.Comp.Loads <= 0)
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("wh40k-neuroware-stick-spent"), source, user.Value);
            return false;
        }

        blank.Comp.Profile = source.Comp.Profile;
        blank.Comp.Integrity = source.Comp.Integrity * CopyIntegrity;
        blank.Comp.Amount = source.Comp.Amount;
        blank.Comp.Rate = source.Comp.Rate;
        blank.Comp.Loads = 1;
        source.Comp.Loads--;
        source.Comp.Integrity *= CopyWear;
        _appearance.SetData(blank, Wh40kNeurowareStickVisuals.Copied, true);
        MarkWritten(blank);

        if (user != null)
            _popup.PopupEntity(Loc.GetString("wh40k-neuroware-copy-done"), blank, user.Value);
        return true;
    }

    private void MarkWritten(Entity<Wh40kNeurowareStickComponent> blank)
    {
        if (blank.Comp.Profile is { } profile && _proto.TryIndex(profile, out var proto) && proto.Legal)
            return;

        _goods.SetNarcotic(blank, true);
    }

    public bool TryStartEncode(Entity<Wh40kNeurowareStickComponent> blank, EntityUid user, EntityUid source)
    {
        if (blank.Comp.Profile != null || !TryFindDrug(source, out _, out _, out _, out _, out var level))
            return false;

        if (level < EncodeMinimum)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-neuroware-encode-short", ("source", source)), source, user);
            return false;
        }

        var args = new DoAfterArgs(EntityManager, user, blank.Comp.EncodeDelay, new Wh40kNeurowareEncodeDoAfterEvent(),
            blank, target: source, used: blank)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        return _doAfter.TryStartDoAfter(args);
    }

    private void OnEncodeDoAfter(Entity<Wh40kNeurowareStickComponent> ent, ref Wh40kNeurowareEncodeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } source)
            return;

        args.Handled = Encode(ent, source, args.User);
    }

    public bool Encode(Entity<Wh40kNeurowareStickComponent> blank, EntityUid source, EntityUid? user)
    {
        if (blank.Comp.Profile != null
            || !TryFindDrug(source, out var soln, out var reagent, out var profile, out var perUnit, out var level)
            || level < EncodeMinimum
            || !_proto.TryIndex(profile, out var proto)
            || proto.Neuroware is not { } twin)
            return false;

        var taken = Math.Min(level, twin.Amount * EncodeLoads);
        var loads = Math.Clamp((int) MathF.Ceiling(taken / twin.Amount), 1, EncodeLoads);
        var solution = soln.Comp.Solution;

        blank.Comp.Profile = profile;
        blank.Comp.Integrity = Math.Clamp(solution.GetPurity(reagent.Prototype), 0f, 1f);
        blank.Comp.Amount = taken / loads;
        blank.Comp.Loads = loads;
        _appearance.SetData(blank, Wh40kNeurowareStickVisuals.Copied, true);
        MarkWritten(blank);

        if (user != null)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-neuroware-encode-done", ("source", source),
                ("program", Loc.GetString(twin.Program))), blank, user.Value);
        }

        _solutions.RemoveReagent(soln, reagent, FixedPoint2.New(taken / perUnit));
        if (solution.Volume <= FixedPoint2.Zero && HasComp<FoodComponent>(source))
            QueueDel(source);

        return true;
    }

    private bool TryFindDrug(EntityUid source, out Entity<SolutionComponent> soln, out ReagentId reagent,
        out ProtoId<Wh40kDrugProfilePrototype> profile, out float perUnit, out float level)
    {
        soln = default;
        reagent = default;
        profile = default;
        perUnit = 0f;
        level = 0f;
        var found = false;

        foreach (var (_, candidate) in _solutions.EnumerateSolutions(source))
        {
            foreach (var (id, quantity) in candidate.Comp.Solution.Contents)
            {
                foreach (var (dosed, each) in _intox.Doses(id.Prototype))
                {
                    var gives = quantity.Float() * each;
                    if (each <= 0f
                        || found && gives <= level
                        || !_proto.TryIndex(dosed, out var proto)
                        || proto.Neuroware == null)
                        continue;

                    found = true;
                    soln = candidate;
                    reagent = id;
                    profile = dosed;
                    perUnit = each;
                    level = gives;
                }
            }
        }

        return found;
    }

    public void Tick(float seconds)
    {
        var query = EntityQueryEnumerator<Wh40kNeurowareComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            for (var i = comp.Programs.Count - 1; i >= 0; i--)
            {
                var program = comp.Programs[i];
                var delta = Math.Min(program.Remaining, program.Rate * seconds);
                program.Remaining -= delta;
                _intox.AddLevel(uid, program.Profile, delta * program.Integrity);
                _intox.AddRush(uid, program.Profile, delta * program.Integrity);

                if (_random.Prob(Math.Clamp((1f - program.Integrity) * GlitchChance * seconds, 0f, 1f)))
                    Glitch((uid, comp));

                if (program.Remaining <= 0f)
                    comp.Programs.RemoveAt(i);
            }

            if (comp.FalseFault != null && _timing.CurTime >= comp.FalseFaultUntil)
                comp.FalseFault = null;

            if (comp.Programs.Count == 0 && comp.FalseFault == null)
                RemCompDeferred<Wh40kNeurowareComponent>(uid);
        }

        var hosts = EntityQueryEnumerator<Wh40kIntoxicationComponent>();
        while (hosts.MoveNext(out var uid, out var intox))
        {
            if (Overdriving(intox) && _intox.IsNeurowareHost(uid))
                Overdrive(uid, seconds);
        }
    }

    private void Glitch(Entity<Wh40kNeurowareComponent> ent)
    {
        switch (_random.Next(4))
        {
            case 0:
                _stun.TryStun(ent, TimeSpan.FromSeconds(1), true);
                break;
            case 1:
                _chat.TryEmoteWithChat(ent, _random.Pick(GlitchEmotes), ignoreActionBlocker: true, forceEmote: true);
                break;
            case 2:
                _status.TryAddStatusEffect(ent, "Stutter", ScrambleTime, true, "ScrambledAccent");
                break;
            default:
                ent.Comp.FalseFault = $"wh40k-neuroware-false-fault-{_random.Next(1, FalseFaults + 1)}";
                ent.Comp.FalseFaultUntil = _timing.CurTime + FalseFaultTime;
                break;
        }
    }

    private bool Overdriving(Wh40kIntoxicationComponent intox)
    {
        foreach (var (profile, level) in intox.Levels)
        {
            if (!_proto.TryIndex(profile, out var proto) || proto.Family != Wh40kDrugFamily.Combat)
                continue;

            var tier = proto.TierAt(level);
            if (tier >= 0 && proto.Tiers[tier].Speed > 1f)
                return true;
        }

        return false;
    }

    private void Overdrive(EntityUid body, float seconds)
    {
        if (TryComp<BatteryComponent>(body, out var own))
            _battery.UseCharge(body, own.MaxCharge * OverdriveDrain * seconds, own);
        else if (_powerCell.TryGetBatteryFromSlot(body, out var cell, out var cellBattery))
            _battery.UseCharge(cell.Value, cellBattery.MaxCharge * OverdriveDrain * seconds, cellBattery);

        _temperature.ChangeHeat(body, OverdriveHeat * seconds, true);
    }

    private void OnCollectFaults(Entity<Wh40kCyberneticHudComponent> ent, ref Wh40kCyberneticHudCollectEvent args)
    {
        if (!_intox.IsNeurowareHost(ent))
            return;

        if (TryComp<Wh40kIntoxicationComponent>(ent, out var intox))
        {
            foreach (var (profile, level) in intox.Levels)
            {
                if (!_proto.TryIndex(profile, out var proto) || proto.Neuroware == null)
                    continue;

                var tier = proto.TierAt(level);
                if (tier < 0)
                    continue;

                var severity = tier == proto.Tiers.Count - 1 ? Wh40kCyberneticSeverity.Crit : Wh40kCyberneticSeverity.Warn;
                foreach (var fault in proto.Neuroware.Faults)
                {
                    Add(args.Faults, fault, severity);
                }
            }

            foreach (var _ in _intox.ActiveCrashes(intox))
            {
                Add(args.Faults, CrashLine, Wh40kCyberneticSeverity.Info);
                break;
            }
        }

        if (TryComp<Wh40kDependenceComponent>(ent, out var dep))
        {
            foreach (var stage in dep.Stage.Values)
            {
                if (stage <= 0)
                    continue;

                Add(args.Faults, HabitLine, stage >= 2 ? Wh40kCyberneticSeverity.Warn : Wh40kCyberneticSeverity.Info);
                break;
            }
        }

        if (CompOrNull<Wh40kNeurowareComponent>(ent)?.FalseFault is { } falseFault)
            Add(args.Faults, falseFault, Wh40kCyberneticSeverity.Crit);
    }

    private static void Add(List<Wh40kCyberneticFault> faults, string line, Wh40kCyberneticSeverity severity)
    {
        foreach (var existing in faults)
        {
            if (existing.Line == line)
                return;
        }

        faults.Add(new Wh40kCyberneticFault(TargetBodyPart.Head, line, severity, AdviceLine));
    }

    private void OnRebooted(Entity<BodyComponent> ent, ref Wh40kChassisRebootedEvent args)
    {
        RemCompDeferred<Wh40kNeurowareComponent>(ent);
        _intox.ClearLevels(ent);
    }
}
