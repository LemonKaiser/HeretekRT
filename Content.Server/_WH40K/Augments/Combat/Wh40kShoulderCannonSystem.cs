using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Emp;
using Content.Shared.Input;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments.Combat;

public sealed partial class Wh40kShoulderCannonSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kShoulderCannonComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kShoulderCannonComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kShoulderCannonComponent, Wh40kToggleShoulderCannonEvent>(OnToggle);
        SubscribeLocalEvent<Wh40kShoulderCannonComponent, Wh40kShoulderCannonDoAfterEvent>(OnCharged);
        SubscribeLocalEvent<Wh40kShoulderCannonComponent, EmpPulseEvent>(OnEmp);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.MouseMiddle, new PointerInputCmdHandler(OnMiddleClick))
            .Register<Wh40kShoulderCannonSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<Wh40kShoulderCannonSystem>();
    }

    private void OnStartup(Entity<Wh40kShoulderCannonComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.DeployActionEntity, ent.Comp.DeployAction);
    }

    private void OnShutdown(Entity<Wh40kShoulderCannonComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            RemComp<Wh40kShoulderCannonDeployedComponent>(ent);

        _actions.RemoveAction(ent.Owner, ent.Comp.DeployActionEntity);
        QueueDel(ent.Comp.DeployActionEntity);
        ent.Comp.DeployActionEntity = null;
    }

    private void OnToggle(Entity<Wh40kShoulderCannonComponent> ent, ref Wh40kToggleShoulderCannonEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        SetDeployed(ent, !HasComp<Wh40kShoulderCannonDeployedComponent>(ent));
    }

    public void SetDeployed(Entity<Wh40kShoulderCannonComponent> ent, bool deployed)
    {
        if (deployed == HasComp<Wh40kShoulderCannonDeployedComponent>(ent))
            return;

        if (deployed)
            EnsureComp<Wh40kShoulderCannonDeployedComponent>(ent);
        else
            RemComp<Wh40kShoulderCannonDeployedComponent>(ent);

        _actions.SetToggled(ent.Comp.DeployActionEntity, deployed);
        _audio.PlayPvs(deployed ? ent.Comp.DeploySound : ent.Comp.StowSound, ent);
    }

    private bool OnMiddleClick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session?.AttachedEntity is { } body &&
            !HasComp<PilotComponent>(body) &&
            TryComp<Wh40kShoulderCannonComponent>(body, out var cannon) &&
            coords.IsValid(EntityManager))
        {
            TryCharge((body, cannon), coords);
        }

        return false;
    }

    public bool TryCharge(Entity<Wh40kShoulderCannonComponent> ent, EntityCoordinates target)
    {
        if (!TryComp<Wh40kShoulderCannonDeployedComponent>(ent, out var deployed) || deployed.Charging)
            return false;

        if (_timing.CurTime < ent.Comp.NextFire)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-shoulder-cannon-cooling"), ent, ent);
            return false;
        }

        var doAfter = new DoAfterArgs(EntityManager, ent, ent.Comp.ChargeTime,
            new Wh40kShoulderCannonDoAfterEvent(GetNetCoordinates(target)), ent)
        {
            BreakOnDamage = false,
            BreakOnMove = false,
            NeedHand = false,
            RequireCanInteract = true,
        };
        if (!_doAfter.TryStartDoAfter(doAfter))
            return false;

        ent.Comp.NextFire = _timing.CurTime + ent.Comp.FireDelay;
        SetCharging((ent, deployed), true);
        _audio.PlayPvs(ent.Comp.ChargeSound, ent);
        return true;
    }

    private void OnCharged(Entity<Wh40kShoulderCannonComponent> ent, ref Wh40kShoulderCannonDoAfterEvent args)
    {
        if (!TryComp<Wh40kShoulderCannonDeployedComponent>(ent, out var deployed))
            return;

        SetCharging((ent, deployed), false);
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        Fire(ent, GetCoordinates(args.Target));
    }

    private void SetCharging(Entity<Wh40kShoulderCannonDeployedComponent> ent, bool charging)
    {
        ent.Comp.Charging = charging;
        Dirty(ent);
    }

    public EntityUid? Fire(Entity<Wh40kShoulderCannonComponent> ent, EntityCoordinates target)
    {
        var from = _transform.GetMapCoordinates(ent);
        var to = _transform.ToMapCoordinates(target);
        if (from.MapId != to.MapId)
            return null;

        var direction = to.Position - from.Position;
        if (direction.LengthSquared() < 0.01f)
            direction = _transform.GetWorldRotation(ent).ToWorldVec();

        var bolt = SpawnAttachedTo(ent.Comp.Bolt, Transform(ent).Coordinates);
        _gun.ShootProjectile(bolt, direction, _physics.GetMapLinearVelocity(ent), ent, ent, ent.Comp.BoltSpeed);
        _audio.PlayPvs(ent.Comp.FireSound, ent);

        var reveal = new Wh40kOpticalCamoRevealEvent();
        RaiseLocalEvent(ent.Owner, ref reveal);
        return bolt;
    }

    private void OnEmp(Entity<Wh40kShoulderCannonComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        SetDeployed(ent, false);
        _actions.SetCooldown(ent.Comp.DeployActionEntity, ent.Comp.EmpLockout);
    }
}
