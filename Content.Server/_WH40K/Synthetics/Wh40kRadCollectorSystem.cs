using Content.Shared._WH40K.Synthetics;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Radiation.Events;
using Content.Shared.Tools.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Synthetics;

/// <summary>
/// Intercepts radiation on cybernetic/IPC chassis into <see cref="Wh40kRadCollectorComponent"/> (up to 300 rads).
/// Overflow degrades collector integrity (0.03 HP/rad) and leaks half into radiation damage; burns out at 0 HP.
/// Pulled out via the chassis bay, the collector vents 5 rad/s and repairs +10 HP per 3s weld.
/// </summary>
public sealed partial class Wh40kRadCollectorSystem : EntitySystem
{
    private static readonly ProtoId<DamageTypePrototype> RadiationType = "Radiation";
    private static readonly TimeSpan WarningCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VentInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ExposureHold = TimeSpan.FromSeconds(2.5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tools = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextWarning = new();
    private readonly Dictionary<EntityUid, (float Rate, TimeSpan At)> _exposure = new();
    private TimeSpan _nextVent;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kChassisComponent, OnIrradiatedEvent>(OnIrradiated);
        SubscribeLocalEvent<Wh40kChassisComponent, ComponentShutdown>(OnChassisShutdown);
        SubscribeLocalEvent<Wh40kRadCollectorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<Wh40kRadCollectorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<Wh40kRadCollectorComponent, Wh40kRadCollectorWeldDoAfterEvent>(OnWeldDone);
    }

    private void OnChassisShutdown(Entity<Wh40kChassisComponent> ent, ref ComponentShutdown args)
    {
        _nextWarning.Remove(ent);
        _exposure.Remove(ent);
    }

    public float GetExposure(EntityUid body) =>
        _exposure.TryGetValue(body, out var last) && _timing.CurTime - last.At <= ExposureHold ? last.Rate : 0f;

    public Entity<Wh40kRadCollectorComponent>? GetCollector(EntityUid body)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (TryComp<Wh40kRadCollectorComponent>(organ, out var collector) && collector.Integrity > 0f)
                return (organ, collector);
        }

        return null;
    }

    public bool IsSaturated(EntityUid body) =>
        GetCollector(body) is { } found && found.Comp.Charge >= found.Comp.Capacity;

    public void FilterRadiationDamage(EntityUid body, ref DamageModifyEvent args)
    {
        var radDamage = args.Damage.DamageDict.GetValueOrDefault(RadiationType);
        if (radDamage <= FixedPoint2.Zero)
            return;

        // While a working collector with free room is installed, radiation is routed to the collector instead.
        if (GetCollector(body) is { } collector && collector.Comp.Charge < collector.Comp.Capacity)
        {
            var modified = new DamageSpecifier(args.Damage);
            modified.DamageDict.Remove(RadiationType);
            args.Damage = modified;
        }
    }

    private void OnIrradiated(EntityUid uid, Wh40kChassisComponent comp, OnIrradiatedEvent args)
    {
        _exposure[uid] = (args.RadsPerSecond, _timing.CurTime);
        if (args.TotalRads > 0f)
            Absorb(uid, args.TotalRads);
    }

    public void Absorb(EntityUid body, float rads)
    {
        if (rads <= 0f)
            return;

        if (GetCollector(body) is not { } found)
        {
            Warn(body, "wh40k-rad-collector-missing");
            return;
        }

        var collector = found.Comp;
        var room = MathF.Max(0f, collector.Capacity - collector.Charge);
        var taken = MathF.Min(rads, room);
        if (taken > 0f)
        {
            collector.Charge += taken;
            Dirty(found.Owner, collector);
        }

        var overflow = rads - taken;
        if (overflow <= 0f)
            return;

        collector.Integrity = MathF.Max(0f, collector.Integrity - overflow * collector.StrainPerRad);
        Dirty(found.Owner, collector);

        var leak = FixedPoint2.New(overflow * collector.LeakDamagePerRad);
        if (leak > FixedPoint2.Zero)
        {
            var leakSpec = new DamageSpecifier(_prototypes.Index(RadiationType), leak);
            _damageable.TryChangeDamage(body, leakSpec, ignoreResistances: true, interruptsDoAfters: false);
        }

        if (collector.Integrity <= 0f)
        {
            Warn(body, "wh40k-rad-collector-burnt-out");
            if (TryComp<OrganComponent>(found.Owner, out var organ))
                _body.RemoveOrgan(found.Owner, organ);
            QueueDel(found.Owner);
        }
        else
        {
            Warn(body, "wh40k-rad-collector-saturated");
        }
    }

    private void Warn(EntityUid body, string key)
    {
        var now = _timing.CurTime;
        if (_nextWarning.TryGetValue(body, out var next) && now < next)
            return;

        _nextWarning[body] = now + WarningCooldown;
        _popup.PopupEntity(Loc.GetString(key), body, body, PopupType.MediumCaution);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextVent)
            return;

        _nextVent = _timing.CurTime + VentInterval;
        var seconds = (float) VentInterval.TotalSeconds;
        var query = EntityQueryEnumerator<Wh40kRadCollectorComponent, OrganComponent>();
        while (query.MoveNext(out var uid, out var collector, out var organ))
        {
            if (organ.Body is { } body)
            {
                DrawExistingBodyRadiation(body, (uid, collector), seconds);
                continue;
            }

            if (collector.Charge <= 0f)
                continue;

            collector.Charge = MathF.Max(0f, collector.Charge - collector.VentPerSecond * seconds);
            Dirty(uid, collector);
        }
    }

    private void DrawExistingBodyRadiation(EntityUid body, Entity<Wh40kRadCollectorComponent> collector, float seconds)
    {
        var room = collector.Comp.Capacity - collector.Comp.Charge;
        if (room <= 0f || collector.Comp.Integrity <= 0f || !HasComp<Wh40kChassisComponent>(body) ||
            !TryComp<DamageableComponent>(body, out var damageable))
        {
            return;
        }

        var currentRad = damageable.Damage.DamageDict.GetValueOrDefault(RadiationType).Float();
        if (currentRad <= 0f)
            return;

        var draw = MathF.Min(MathF.Min(currentRad, room), collector.Comp.DrawPerSecond * seconds);
        var amount = FixedPoint2.New(draw);
        if (amount <= FixedPoint2.Zero)
            return;

        var heal = new DamageSpecifier(_prototypes.Index(RadiationType), -amount);
        _damageable.TryChangeDamage(body, heal, ignoreResistances: true, interruptsDoAfters: false);
        collector.Comp.Charge += draw;
        Dirty(collector);
    }

    private void OnExamined(Entity<Wh40kRadCollectorComponent> ent, ref ExaminedEvent args)
    {
        var chargePct = ent.Comp.Capacity > 0f ? (int) MathF.Round(ent.Comp.Charge / ent.Comp.Capacity * 100f) : 0;
        var integrityPct = ent.Comp.MaxIntegrity > 0f ? (int) MathF.Round(ent.Comp.Integrity / ent.Comp.MaxIntegrity * 100f) : 0;
        args.PushMarkup(Loc.GetString("wh40k-rad-collector-examine-charge", ("percent", chargePct)));
        args.PushMarkup(Loc.GetString("wh40k-rad-collector-examine-integrity", ("percent", integrityPct)));

        if (CompOrNull<OrganComponent>(ent)?.Body == null && ent.Comp.Charge > 0f)
            args.PushMarkup(Loc.GetString("wh40k-rad-collector-examine-venting"));
    }

    private void OnInteractUsing(Entity<Wh40kRadCollectorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || CompOrNull<OrganComponent>(ent)?.Body != null ||
            !_tools.HasQuality(args.Used, "Welding"))
        {
            return;
        }

        if (ent.Comp.Integrity >= ent.Comp.MaxIntegrity)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-rad-collector-weld-full"), ent, args.User);
            args.Handled = true;
            return;
        }

        args.Handled = _tools.UseTool(
            args.Used,
            args.User,
            ent,
            ent.Comp.WeldTime,
            new[] { "Welding" },
            new Wh40kRadCollectorWeldDoAfterEvent(),
            fuel: 2f);
    }

    private void OnWeldDone(Entity<Wh40kRadCollectorComponent> ent, ref Wh40kRadCollectorWeldDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        ent.Comp.Integrity = MathF.Min(ent.Comp.MaxIntegrity, ent.Comp.Integrity + ent.Comp.WeldRepair);
        Dirty(ent);

        var pct = ent.Comp.MaxIntegrity > 0f ? (int) MathF.Round(ent.Comp.Integrity / ent.Comp.MaxIntegrity * 100f) : 100;
        _popup.PopupEntity(Loc.GetString("wh40k-rad-collector-welded", ("percent", pct)), ent, args.User);
    }
}
