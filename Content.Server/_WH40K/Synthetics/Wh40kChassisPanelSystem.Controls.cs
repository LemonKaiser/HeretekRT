using Content.Server.Power.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.Body.Part;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Jittering;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Synthetics;

public sealed partial class Wh40kChassisPanelSystem
{
    public static readonly TimeSpan RebootTime = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan RebootCooldown = TimeSpan.FromMinutes(2);
    public const float RebootCost = 0.10f;

    public static readonly TimeSpan OverclockTick = TimeSpan.FromSeconds(5);
    public const float OverclockDrawMultiplier = 3f;
    public const float OverclockFallbackDrain = 0.02f;
    public const float OverclockMinCharge = 0.15f;
    public const float OverclockHeatPerTick = 14f;
    public const float OverclockHeatLimit = 100f;
    public const float OverclockSparkChanceBase = 0.15f;
    public const float OverclockSparkCertainHeat = 75f;
    public const float OverclockShortFromHeat = 45f;
    public const float OverclockCookFromHeat = 75f;
    public static readonly FixedPoint2 OverclockCookDamage = FixedPoint2.New(3);
    public const float OverclockTempHeatPerTick = 4500f;
    public const float PassiveCoolPerSecond = 2.5f;

    public static readonly EntProtoId SparkEffect = "EffectSparks";
    private static readonly ProtoId<DamageTypePrototype> CookDamageType = "Heat";

    private static readonly SoundSpecifier DetachSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");
    private static readonly SoundSpecifier RebootStartSound = new SoundPathSpecifier("/Audio/Machines/quickbeep.ogg");
    private static readonly SoundSpecifier RebootDoneSound = new SoundPathSpecifier("/Audio/Machines/twobeep.ogg");
    private static readonly SoundSpecifier OverclockSound = new SoundPathSpecifier("/Audio/Machines/beep.ogg");
    private static readonly SoundSpecifier SparkSound = new SoundCollectionSpecifier("sparks");

    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private void OnIsolateMessage(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisIsolatePartMessage args)
    {
        if (!ValidatePanelControl(ent, args.Actor))
            return;

        if (TryGetEntity(args.Part, out var part))
            SetIsolated(ent, part.Value, args.Isolate, args.Actor);
    }

    private void OnDetachMessage(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisDetachPartMessage args)
    {
        if (!ValidatePanelControl(ent, args.Actor))
            return;

        if (TryGetEntity(args.Part, out var part))
            TryDetach(ent, part.Value, args.Actor);
    }

    private void OnRebootMessage(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisRebootMessage args)
    {
        if (!ValidatePanelControl(ent, args.Actor))
            return;

        TryReboot(ent, args.Actor);
    }

    private void OnOverclockMessage(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisOverclockMessage args)
    {
        if (!ValidatePanelControl(ent, args.Actor))
            return;

        SetOverclock(ent, args.On, args.Actor);
    }

    private void OnInstallAugmentMessage(Entity<Wh40kChassisComponent> ent, ref Wh40kChassisInstallAugmentMessage args)
    {
        if (!ValidatePanelControl(ent, args.Actor))
            return;

        if (!_augmentBed.IsOnPoweredTable(ent))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-table-required-install"), ent, args.Actor);
            PushState(ent);
            return;
        }

        if (TryGetEntity(args.Item, out var item) && _augmentBed.TryStartFromChassisPanel(ent, item.Value, args.Zone))
            return;

        _popup.PopupEntity(Loc.GetString("wh40k-augment-bed-unavailable"), ent, args.Actor);
        PushState(ent);
    }

    private bool ValidatePanelControl(EntityUid body, EntityUid user)
    {
        if (CanUsePanel(body, user))
            return true;

        _ui.CloseUi(body, Wh40kChassisPanelUiKey.Key, user);
        if (user == body)
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-panel-refuse-dead"), body, user);
        return false;
    }

    public bool SetIsolated(EntityUid body, EntityUid part, bool isolate, EntityUid user)
    {
        if (CompOrNull<BodyPartComponent>(part) is not { } bodyPart || bodyPart.Body != body)
            return false;

        if (isolate &&
            bodyPart.PartType is BodyPartType.Arm or BodyPartType.Hand &&
            CompOrNull<CuffableComponent>(body)?.CuffedHandCount > 0)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-isolate-refuse-cuffed"), body, user);
            return false;
        }

        if (!_isolation.TrySetUserIsolated(part, isolate, out var deferred))
        {
            if (isolate)
                _popup.PopupEntity(Loc.GetString("wh40k-chassis-isolate-refuse"), body, user);
            return false;
        }

        var key = deferred && !isolate
            ? "wh40k-chassis-restore-low-power"
            : isolate ? "wh40k-chassis-isolate-done" : "wh40k-chassis-restore-done";
        _popup.PopupEntity(Loc.GetString(key, ("part", part)), body, user);
        PushState(body);
        return true;
    }

    public bool TryDetach(EntityUid body, EntityUid part, EntityUid user)
    {
        if (!_augmentBed.IsOnPoweredTable(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-table-required-detach"), body, user);
            return false;
        }

        if (!TryComp<BodyPartComponent>(part, out var bodyPart) ||
            bodyPart.Body != body ||
            !Wh40kChassisIsolationSystem.CanIsolate(bodyPart) ||
            !_limbs.TryDetach(body, part))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-detach-refuse"), body, user);
            return false;
        }

        _audio.PlayPvs(DetachSound, body);
        _popup.PopupEntity(Loc.GetString("wh40k-chassis-detach-done", ("part", part), ("target", body)), body, user);
        if (user != body)
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-chassis-detach-done-owner", ("part", part)),
                body,
                body,
                PopupType.MediumCaution);
        }

        PushState(body);
        return true;
    }

    public bool TryReboot(EntityUid body, EntityUid user)
    {
        if (_mobState.IsDead(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-reboot-refuse-dead"), body, user);
            return false;
        }

        if (HasComp<Wh40kChassisRebootComponent>(body))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-reboot-refuse-busy"), body, user);
            return false;
        }

        TryComp<Wh40kChassisComponent>(body, out var chassis);
        if (chassis != null && _timing.CurTime < chassis.NextReboot)
        {
            var wait = (int) Math.Ceiling((chassis.NextReboot - _timing.CurTime).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-reboot-refuse-cooldown", ("seconds", wait)), body, user);
            return false;
        }

        if (chassis != null)
        {
            chassis.NextReboot = _timing.CurTime + RebootCooldown;
            if (chassis.Overclocked)
                SetOverclock(body, false, user, quiet: true);
        }

        if (TryGetCell(body, out var cell))
            _battery.ChangeCharge(cell.Value.Owner, -cell.Value.Comp.MaxCharge * RebootCost, cell.Value.Comp);

        EnsureComp<Wh40kChassisRebootComponent>(body).EndTime = _timing.CurTime + RebootTime;
        _stun.TryParalyze(body, RebootTime, true);
        _audio.PlayPvs(RebootStartSound, body);
        _popup.PopupEntity(Loc.GetString("wh40k-chassis-reboot-start", ("target", body)), body, PopupType.Medium);
        PushState(body);
        return true;
    }

    private void UpdateReboots(TimeSpan now)
    {
        var done = new List<EntityUid>();
        var query = EntityQueryEnumerator<Wh40kChassisRebootComponent>();
        while (query.MoveNext(out var uid, out var reboot))
        {
            if (now >= reboot.EndTime)
                done.Add(uid);
        }

        foreach (var uid in done)
            FinishReboot(uid);
    }

    public void FinishReboot(EntityUid body)
    {
        RemComp<Wh40kChassisRebootComponent>(body);
        if (TerminatingOrDeleted(body))
            return;

        _statusEffects.TryRemoveStatusEffect(body, "Stun");
        _statusEffects.TryRemoveStatusEffect(body, "KnockedDown");
        _statusEffects.TryRemoveStatusEffect(body, "Jitter");
        _statusEffects.TryRemoveStatusEffect(body, "Stutter");
        _statusEffects.TryRemoveStatusEffect(body, "Stuttering");
        _statusEffects.TryRemoveStatusEffect(body, "TemporaryBlindness");

        if (TryComp<StaminaComponent>(body, out var stamina))
        {
            stamina.StaminaDamage = 0f;
            stamina.Critical = false;
            RemComp<ActiveStaminaComponent>(body);
            Dirty(body, stamina);
        }

        if (TryComp<Wh40kChassisComponent>(body, out var chassis) && chassis.ChassisHeat > 0f)
        {
            chassis.ChassisHeat = MathF.Max(0f, chassis.ChassisHeat * 0.5f);
            Dirty(body, chassis);
        }

        var ev = new Wh40kChassisRebootedEvent();
        RaiseLocalEvent(body, ref ev);
        _audio.PlayPvs(RebootDoneSound, body);
        _popup.PopupEntity(Loc.GetString("wh40k-chassis-reboot-done"), body, body);
        PushState(body);
    }

    public bool SetOverclock(EntityUid body, bool on, EntityUid user, bool quiet = false)
    {
        if (!TryComp<Wh40kChassisComponent>(body, out var chassis) || chassis.Overclocked == on)
            return false;

        if (on && OverclockBlocker(body, chassis) is { } refusal)
        {
            _popup.PopupEntity(Loc.GetString(refusal), body, user);
            return false;
        }

        chassis.Overclocked = on;
        Dirty(body, chassis);

        if (on)
            EnsureComp<Wh40kChassisOverclockComponent>(body).NextTick = _timing.CurTime + OverclockTick;
        else
            RemComp<Wh40kChassisOverclockComponent>(body);

        _speed.RefreshMovementSpeedModifiers(body);
        if (!quiet)
        {
            _audio.PlayPvs(OverclockSound, body);
            _popup.PopupEntity(Loc.GetString(on ? "wh40k-chassis-overclock-on" : "wh40k-chassis-overclock-off"), body, user);
        }

        PushState(body);
        return true;
    }

    private string? OverclockBlocker(EntityUid body, Wh40kChassisComponent chassis)
    {
        if (_mobState.IsDead(body) || _mobState.IsCritical(body))
            return "wh40k-chassis-overclock-refuse-dead";

        if (HasComp<Wh40kChassisRebootComponent>(body))
            return "wh40k-chassis-reboot-refuse-busy";

        if (HasComp<Wh40kLowPowerComponent>(body))
            return "wh40k-chassis-overclock-refuse-low-power";

        if (!TryGetCell(body, out var cell) || ChargeFraction(cell.Value.Comp) < OverclockMinCharge)
            return "wh40k-chassis-overclock-refuse-charge";

        if (chassis.ChassisHeat >= OverclockHeatLimit)
            return "wh40k-chassis-overclock-refuse-heat";

        return null;
    }

    private void UpdateOverclock(TimeSpan now)
    {
        var stop = new List<(EntityUid Body, string Reason)>();
        var query = EntityQueryEnumerator<Wh40kChassisOverclockComponent, Wh40kChassisComponent>();
        while (query.MoveNext(out var uid, out var overclock, out var chassis))
        {
            if (now >= overclock.NextTick)
            {
                overclock.NextTick = now + OverclockTick;
                ApplyOverclockTick(uid, chassis);
            }

            if (OverclockBlocker(uid, chassis) is { } reason)
                stop.Add((uid, reason));
        }

        foreach (var (body, reason) in stop)
        {
            SetOverclock(body, false, body, quiet: true);
            _popup.PopupEntity(
                Loc.GetString("wh40k-chassis-overclock-auto-off", ("reason", Loc.GetString(reason))),
                body,
                body,
                PopupType.MediumCaution);
        }
    }

    public void ApplyOverclockTick(EntityUid body, Wh40kChassisComponent chassis)
    {
        if (TryGetCell(body, out var cell))
        {
            var draw = CompOrNull<SiliconComponent>(body)?.DrainPerSecond;
            var drain = draw is { } rate && rate > 0f
                ? rate * (OverclockDrawMultiplier - 1f) * (float) OverclockTick.TotalSeconds
                : cell.Value.Comp.MaxCharge * OverclockFallbackDrain;
            _battery.ChangeCharge(cell.Value.Owner, -drain, cell.Value.Comp);
        }

        chassis.ChassisHeat = MathF.Min(OverclockHeatLimit, chassis.ChassisHeat + OverclockHeatPerTick);
        Dirty(body, chassis);

        if (TryComp<TemperatureComponent>(body, out var temperature))
            _temperature.ChangeHeat(body, OverclockTempHeatPerTick, temperature: temperature);

        var heat = chassis.ChassisHeat;
        var sparkChance = heat >= OverclockSparkCertainHeat
            ? 1f
            : OverclockSparkChanceBase + (1f - OverclockSparkChanceBase) * (heat / OverclockSparkCertainHeat);
        if (_random.Prob(Math.Clamp(sparkChance, 0f, 1f)))
        {
            Spawn(SparkEffect, _transform.GetMapCoordinates(body));
            _audio.PlayPvs(SparkSound, body, AudioParams.Default.WithVariation(0.1f));
        }

        if (heat > OverclockShortFromHeat &&
            _random.Prob(Math.Clamp((heat - OverclockShortFromHeat) / (OverclockHeatLimit - OverclockShortFromHeat), 0f, 1f)))
        {
            _stun.TryStun(body, TimeSpan.FromSeconds(0.9), true);
            _jitter.DoJitter(body, TimeSpan.FromSeconds(1.5), true, amplitude: 12f, frequency: 8f);
            _popup.PopupEntity(Loc.GetString("wh40k-chassis-overclock-short"), body, body, PopupType.MediumCaution);
        }

        if (heat >= OverclockCookFromHeat)
        {
            _damageable.TryChangeDamage(
                body,
                new DamageSpecifier(_prototypes.Index(CookDamageType), OverclockCookDamage),
                interruptsDoAfters: false);
        }
    }

    private void CoolChassisHeat(float seconds)
    {
        var query = EntityQueryEnumerator<Wh40kChassisComponent>();
        while (query.MoveNext(out var uid, out var chassis))
        {
            if (chassis.Overclocked || chassis.ChassisHeat <= 0f)
                continue;

            var coolMult = TryComp<TemperatureComponent>(uid, out var temp) && temp.CurrentTemperature < 273.15f ? 2f : 1f;
            chassis.ChassisHeat = MathF.Max(0f, chassis.ChassisHeat - PassiveCoolPerSecond * coolMult * seconds);
            Dirty(uid, chassis);
        }
    }
}

[RegisterComponent]
public sealed partial class Wh40kChassisRebootComponent : Component
{
    [DataField]
    public TimeSpan EndTime;
}

[RegisterComponent]
public sealed partial class Wh40kChassisOverclockComponent : Component
{
    [DataField]
    public TimeSpan NextTick;
}
