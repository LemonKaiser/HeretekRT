using System.Linq;
using Content.Server._EinsteinEngines.Silicon.Charge;
using Content.Server._EinsteinEngines.Silicon.Death;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._EinsteinEngines.Silicon.Systems;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Synthetics;

/// <summary>
/// Handles empty/missing battery on Cybernetic (Mechanicus) and IPC chassis:
/// keeps the player alive and conscious, slows movement to 25%, and powers only one arm
/// (switchable via right-click verb with a 3-second cooldown).
/// </summary>
public sealed class Wh40kLowPowerSystem : SharedWh40kSyntheticsSystem
{
    public static readonly TimeSpan SwitchCooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SiliconChargeSystem _siliconCharge = default!;
    [Dependency] private readonly Wh40kChassisIsolationSystem _isolation = default!;

    private TimeSpan _nextSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kLowPowerComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<Wh40kChassisComponent, SiliconChargeStateUpdateEvent>(OnChargeStateUpdate);
        SubscribeLocalEvent<SiliconDownOnDeadComponent, SiliconChargeDyingEvent>(OnChargeDying);
    }

    private void OnChargeDying(EntityUid uid, SiliconDownOnDeadComponent comp, SiliconChargeDyingEvent args)
    {
        if (HasComp<Wh40kChassisComponent>(uid) || comp.Dead)
            args.Cancel();
    }

    private void OnChargeStateUpdate(Entity<Wh40kChassisComponent> ent, ref SiliconChargeStateUpdateEvent args)
    {
        RefreshPowerState(ent);
    }

    public void RefreshPowerState(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !IsSyntheticChassis(body))
        {
            Exit(body);
            return;
        }

        if (!TryComp<SiliconComponent>(body, out var silicon) || !silicon.BatteryPowered)
        {
            Exit(body);
            return;
        }

        var hasCharge = _siliconCharge.TryGetSiliconBattery(body, out var battery) && battery.CurrentCharge > 0.01f;
        SetLowPower(body, !hasCharge);
    }

    public bool SetLowPower(EntityUid body, bool low)
    {
        if (!low)
        {
            Exit(body);
            return true;
        }

        if (TerminatingOrDeleted(body) || !IsSyntheticChassis(body))
            return false;

        if (HasComp<Wh40kLowPowerComponent>(body))
            return true;

        var comp = AddComp<Wh40kLowPowerComponent>(body);
        comp.PoweredArm = PickArm(body);
        Dirty(body, comp);
        Reconcile((body, comp));
        _speed.RefreshMovementSpeedModifiers(body);
        _popup.PopupEntity(Loc.GetString("wh40k-low-power-enter"), body, body, PopupType.LargeCaution);
        return true;
    }

    private void Exit(EntityUid body)
    {
        if (!HasComp<Wh40kLowPowerComponent>(body))
            return;

        if (TerminatingOrDeleted(body))
        {
            RemComp<Wh40kLowPowerComponent>(body);
            return;
        }

        foreach (var (arm, _) in _body.GetBodyChildrenOfType(body, BodyPartType.Arm).ToList())
            _isolation.TryRestorePower(arm);

        RemComp<Wh40kLowPowerComponent>(body);
        _speed.RefreshMovementSpeedModifiers(body);
        _popup.PopupEntity(Loc.GetString("wh40k-low-power-exit"), body, body);
    }

    public bool TrySwitchArm(Entity<Wh40kLowPowerComponent?> body, EntityUid arm)
    {
        if (!Resolve(body, ref body.Comp, false) || body.Comp.PoweredArm == arm ||
            CompOrNull<BodyPartComponent>(arm) is not { PartType: BodyPartType.Arm } part || part.Body != body.Owner)
        {
            return false;
        }

        if (_timing.CurTime < body.Comp.NextSwitch)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-low-power-switch-cooldown"), body, body);
            return false;
        }

        body.Comp.PoweredArm = arm;
        body.Comp.NextSwitch = _timing.CurTime + SwitchCooldown;
        Dirty(body);
        Reconcile((body, body.Comp));
        _popup.PopupEntity(Loc.GetString("wh40k-low-power-switched", ("part", arm)), body, body);
        return true;
    }

    private EntityUid? PickArm(EntityUid body)
    {
        var arms = _body.GetBodyChildrenOfType(body, BodyPartType.Arm).ToList();
        if (arms.Count == 0)
            return null;

        var free = arms.Where(arm => !_isolation.IsUserIsolated(arm.Id)).ToList();
        if (free.Count > 0)
            arms = free;

        if (CompOrNull<HandsComponent>(body)?.ActiveHand is { } active)
        {
            var side = active.Location switch
            {
                HandLocation.Left => BodyPartSymmetry.Left,
                HandLocation.Right => BodyPartSymmetry.Right,
                _ => BodyPartSymmetry.None,
            };

            foreach (var (arm, part) in arms)
            {
                if (part.Symmetry == side)
                    return arm;
            }
        }

        foreach (var (arm, part) in arms)
        {
            if (_body.GetBodyPartChildren(arm, part).Any(child => child.Component.PartType == BodyPartType.Hand))
                return arm;
        }

        return arms[0].Id;
    }

    private void Reconcile(Entity<Wh40kLowPowerComponent> ent)
    {
        var arms = _body.GetBodyChildrenOfType(ent, BodyPartType.Arm).ToList();
        if (ent.Comp.PoweredArm is not { } powered || arms.All(arm => arm.Id != powered))
        {
            ent.Comp.PoweredArm = PickArm(ent);
            Dirty(ent);
        }

        var changed = false;
        foreach (var (arm, _) in arms)
        {
            changed |= arm == ent.Comp.PoweredArm
                ? _isolation.TryRestorePower(arm)
                : _isolation.TryCutPower(arm);
        }

        if (changed)
            _speed.RefreshMovementSpeedModifiers(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;

        var chassisQuery = EntityQueryEnumerator<Wh40kChassisComponent>();
        var toCheck = new List<EntityUid>();
        while (chassisQuery.MoveNext(out var uid, out _))
            toCheck.Add(uid);

        foreach (var uid in toCheck)
            RefreshPowerState(uid);

        var lowQuery = EntityQueryEnumerator<Wh40kLowPowerComponent>();
        var live = new List<Entity<Wh40kLowPowerComponent>>();
        while (lowQuery.MoveNext(out var uid, out var comp))
        {
            if (!TerminatingOrDeleted(uid) && IsSyntheticChassis(uid))
                live.Add((uid, comp));
        }

        foreach (var ent in live)
            Reconcile(ent);
    }

    private void OnGetVerbs(Entity<Wh40kLowPowerComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (args.User != ent.Owner || !args.CanInteract)
            return;

        foreach (var (arm, _) in _body.GetBodyChildrenOfType(ent, BodyPartType.Arm))
        {
            if (arm == ent.Comp.PoweredArm)
                continue;

            var target = arm;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wh40k-low-power-verb-switch", ("part", target)),
                Act = () => TrySwitchArm(ent.Owner, target),
            });
        }
    }
}
