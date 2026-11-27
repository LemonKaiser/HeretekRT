using Content.Server._Goobstation.PairedExtendable;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Actions;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Emp;
using Content.Shared.Hands.Components;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments.Combat;

/// <summary>
/// Manages deployable limb/hand implants (Arm Laser, Hand Shield, Finger Lighter):
/// grants side-appropriate actions, folds items in/out of the matching hand, preserves battery charge while stowed,
/// retracts and locks out on EMP, and supports Q-key retraction.
/// </summary>
public sealed class Wh40kDeployableImplantSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PairedExtendableSystem _extendable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subscribe<Wh40kFingerLighterComponent>();
        Subscribe<Wh40kArmLaserComponent>();
        Subscribe<Wh40kHandShieldComponent>();
    }

    private void Subscribe<T>() where T : Wh40kDeployableImplantComponent
    {
        SubscribeLocalEvent<T, ComponentStartup>((Entity<T> ent, ref ComponentStartup _) => Grant(ent, ent.Comp));
        SubscribeLocalEvent<T, ComponentShutdown>((Entity<T> ent, ref ComponentShutdown _) => Revoke(ent, ent.Comp));
        SubscribeLocalEvent<T, OrganAddedToBodyEvent>((Entity<T> ent, ref OrganAddedToBodyEvent _) => Grant(ent, ent.Comp));
        SubscribeLocalEvent<T, OrganRemovedFromBodyEvent>((Entity<T> ent, ref OrganRemovedFromBodyEvent args) => Revoke(ent, ent.Comp, args.OldBody));
        SubscribeLocalEvent<T, BodyPartComponentsModifyEvent>((Entity<T> ent, ref BodyPartComponentsModifyEvent args) =>
        {
            if (args.Add)
                Grant(ent, ent.Comp);
            else
                Revoke(ent, ent.Comp, args.Body);
        });
        SubscribeLocalEvent<T, Wh40kToggleDeployableImplantEvent>((Entity<T> ent, ref Wh40kToggleDeployableImplantEvent args) => Toggle(ent, ent.Comp, args));
        SubscribeLocalEvent<T, EmpPulseEvent>((Entity<T> ent, ref EmpPulseEvent args) => OnEmp(ent, ent.Comp, ref args));
        SubscribeLocalEvent<T, EntityUnpausedEvent>((Entity<T> ent, ref EntityUnpausedEvent args) =>
        {
            if (ent.Comp.EmpDisabledUntil > TimeSpan.Zero)
                ent.Comp.EmpDisabledUntil += args.PausedTime;
        });
    }

    private void OnEmp(EntityUid implantUid, Wh40kDeployableImplantComponent implant, ref EmpPulseEvent args)
    {
        args.Affected = true;
        var until = _timing.CurTime + args.Duration;
        if (until > implant.EmpDisabledUntil)
            implant.EmpDisabledUntil = until;

        Retract(implant);
    }

    private void Grant(EntityUid implantUid, Wh40kDeployableImplantComponent implant)
    {
        if (!TryResolveHost(implantUid, out var body, out _, out var side))
            return;

        if (implant.ActionEntity != null)
            return;

        var actionId = PickAction(implant, side);
        if (actionId == null)
            return;

        _actions.AddAction(body, ref implant.ActionEntity, actionId.Value, implantUid);
    }

    private void Revoke(EntityUid implantUid, Wh40kDeployableImplantComponent implant, EntityUid? explicitBody = null)
    {
        Retract(implant);

        var body = explicitBody;
        if (body == null && TryResolveHost(implantUid, out var resolvedBody, out _, out _))
            body = resolvedBody;

        if (body is { Valid: true } owner && !TerminatingOrDeleted(owner) && implant.ActionEntity != null)
            _actions.RemoveAction(owner, implant.ActionEntity);

        QueueDel(implant.ActionEntity);
        implant.ActionEntity = null;
    }

    private void Retract(Wh40kDeployableImplantComponent implant)
    {
        StoreCharge(implant);
        QueueDel(implant.Deployed);
        implant.Deployed = null;
    }

    public void Stow(EntityUid implantUid, Wh40kDeployableImplantComponent implant)
    {
        if (!Exists(implant.Deployed))
            return;

        Retract(implant);
        _audio.PlayPvs(implant.RetractSound, implantUid);
    }

    /// <summary>
    /// Retracts <paramref name="item"/> if it was deployed by any deployable augment installed in <paramref name="body"/>.
    /// </summary>
    public bool TryStowBodyItem(EntityUid body, EntityUid item)
    {
        foreach (var (partUid, part) in _body.GetBodyChildren(body))
        {
            if (TryStowEntityItem(partUid, item))
                return true;

            foreach (var (organUid, _) in _body.GetPartOrgans(partUid, part))
            {
                if (TryStowEntityItem(organUid, item))
                    return true;
            }
        }

        return false;
    }

    private bool TryStowEntityItem(EntityUid hostUid, EntityUid item)
    {
        foreach (var implant in EntityManager.GetComponents<Wh40kDeployableImplantComponent>(hostUid))
        {
            if (implant.Deployed != item)
                continue;

            Stow(hostUid, implant);
            return true;
        }

        return false;
    }

    private bool CanDeploy(EntityUid implantUid, EntityUid body)
    {
        var ev = new Wh40kDeployAttemptEvent(body);
        RaiseLocalEvent(implantUid, ref ev);
        return !ev.Cancelled;
    }

    private void StoreCharge(Wh40kDeployableImplantComponent implant)
    {
        if (implant.KeepCharge && TryComp<BatteryComponent>(implant.Deployed, out var battery))
            implant.StoredCharge = (battery.CurrentCharge, _timing.CurTime);
    }

    private void RestoreCharge(Wh40kDeployableImplantComponent implant)
    {
        if (implant.Deployed is not { } item ||
            !TryComp<BatteryComponent>(item, out var battery))
        {
            return;
        }

        var rate = CompOrNull<BatterySelfRechargerComponent>(item)?.AutoRechargeRate ?? 0f;
        if (GetStowedCharge(implant, battery, rate) is { } charge)
            _battery.SetCharge(item, charge, battery);
    }

    internal float? GetPersistentCharge(Wh40kArmLaserComponent implant)
    {
        if (TryComp<BatteryComponent>(implant.Deployed, out var deployed))
            return deployed.CurrentCharge;

        return TryGetLaserBattery(implant, out var battery, out var rate)
            ? GetStowedCharge(implant, battery, rate)
            : null;
    }

    internal bool TryRestorePersistentCharge(Wh40kArmLaserComponent implant, float charge)
    {
        if (!float.IsFinite(charge) || charge < 0f ||
            !TryGetLaserBattery(implant, out var battery, out _) || charge > battery.MaxCharge)
            return false;

        implant.StoredCharge = (charge, _timing.CurTime);
        return true;
    }

    private bool TryGetLaserBattery(Wh40kArmLaserComponent implant,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BatteryComponent? battery, out float rate)
    {
        battery = null;
        rate = 0f;
        if (implant.Items.Count == 0 || !_prototypes.TryIndex(implant.Items[0], out var item) ||
            !item.TryGetComponent(out battery, _componentFactory))
            return false;

        if (item.TryGetComponent<BatterySelfRechargerComponent>(out var recharger, _componentFactory))
            rate = recharger.AutoRechargeRate;

        return true;
    }

    private float? GetStowedCharge(Wh40kDeployableImplantComponent implant, BatteryComponent battery, float rate)
    {
        if (implant.StoredCharge is not { } stored)
            return null;

        var elapsed = Math.Max(0f, (float) (_timing.CurTime - stored.At).TotalSeconds);
        return Math.Clamp(stored.Charge + rate * elapsed, 0f, battery.MaxCharge);
    }

    private void Toggle(EntityUid implantUid, Wh40kDeployableImplantComponent implant, Wh40kToggleDeployableImplantEvent args)
    {
        if (args.Action.Owner != implant.ActionEntity ||
            !TryResolveHost(implantUid, out var body, out var partUid, out var side))
        {
            return;
        }

        if (_timing.CurTime < implant.EmpDisabledUntil ||
            HasComp<EmpDisabledComponent>(implantUid) ||
            HasComp<EmpDisabledComponent>(partUid))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-augment-deployable-emp"), body, body);
            return;
        }

        if (implant.Items.Count == 0 || (!Exists(implant.Deployed) && !CanDeploy(implantUid, body)))
            return;

        StoreCharge(implant);
        args.Handled = _extendable.ToggleExtendable(body, implant.Items[0], side, out implant.Deployed, implant.Deployed);
        if (!args.Handled)
            return;

        RestoreCharge(implant);
        _audio.PlayPvs(implant.Deployed == null ? implant.RetractSound : implant.DeploySound, body);
    }

    public bool TryResolveHost(EntityUid implantUid, out EntityUid body, out EntityUid partUid, out HandLocation side)
    {
        body = default;
        partUid = default;
        side = HandLocation.Middle;

        BodyPartComponent? partComp = null;
        if (TryComp<OrganComponent>(implantUid, out var organ))
        {
            if (organ.Body is not { } owner)
                return false;

            body = owner;
            partUid = Transform(implantUid).ParentUid;
            TryComp(partUid, out partComp);
        }
        else if (TryComp(implantUid, out partComp))
        {
            if (partComp.Body is not { } owner)
                return false;

            body = owner;
            partUid = implantUid;
        }
        else
        {
            return false;
        }

        side = partComp?.Symmetry switch
        {
            BodyPartSymmetry.Left => HandLocation.Left,
            BodyPartSymmetry.Right => HandLocation.Right,
            _ => HandLocation.Middle,
        };
        return true;
    }

    private static EntProtoId? PickAction(Wh40kDeployableImplantComponent implant, HandLocation side) => side switch
    {
        HandLocation.Left => implant.LeftAction ?? implant.Action,
        HandLocation.Right => implant.RightAction ?? implant.Action,
        _ => implant.Action ?? implant.LeftAction ?? implant.RightAction,
    };
}
