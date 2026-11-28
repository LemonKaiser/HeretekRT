using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments.Combat;

public sealed partial class Wh40kBurstLauncherSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<PhysicsComponent>> _caught = new();

    public override void Initialize()
    {
        base.Initialize();

        Subscribe<Wh40kHaloBurstComponent>();
        Subscribe<Wh40kHaloStunComponent>();
    }

    private void Subscribe<T>() where T : Wh40kBurstLauncherComponent
    {
        SubscribeLocalEvent<T, ComponentStartup>((uid, comp, _) =>
            _actions.AddAction(uid, ref comp.ActionEntity, comp.Action));
        SubscribeLocalEvent<T, ComponentShutdown>((uid, comp, _) =>
        {
            _actions.RemoveAction(uid, comp.ActionEntity);
            QueueDel(comp.ActionEntity);
            comp.ActionEntity = null;
        });
        SubscribeLocalEvent<T, Wh40kFireBurstLauncherEvent>((uid, comp, args) => OnFire((uid, comp), args));
    }

    private void OnFire(Entity<Wh40kBurstLauncherComponent> ent, Wh40kFireBurstLauncherEvent args)
    {
        if (args.Handled || args.Action.Owner != ent.Comp.ActionEntity || HasComp<Wh40kHaloArmingComponent>(ent))
            return;

        args.Handled = true;
        Launch(ent);
    }

    public void Launch(Entity<Wh40kBurstLauncherComponent> ent)
    {
        var detonateAt = _timing.CurTime + ent.Comp.Fuse;
        var arming = EnsureComp<Wh40kHaloArmingComponent>(ent);
        arming.End = detonateAt;
        arming.Shield = SpawnAttachedTo(ent.Comp.Shield, new EntityCoordinates(ent, Vector2.Zero));
        Dirty(ent, arming);

        var origin = _transform.GetMapCoordinates(ent);
        var segment = 360f / Math.Max(ent.Comp.Orbs, 1);
        var turn = _random.NextFloat(segment);
        var speed = 2f * ent.Comp.OrbDistance / (float) ent.Comp.OrbFlight.TotalSeconds;
        for (var i = 0; i < ent.Comp.Orbs; i++)
        {
            var orb = Spawn(ent.Comp.Orb, origin);
            if (!TryComp<Wh40kHaloOrbComponent>(orb, out var charge) || !TryComp<PhysicsComponent>(orb, out var body))
                continue;

            charge.DetonateAt = detonateAt;
            charge.Source = ent;
            charge.LaunchVelocity = Angle.FromDegrees(turn + segment * i).ToVec() * speed;
            charge.LaunchedAt = _timing.CurTime;
            charge.StopAt = _timing.CurTime + ent.Comp.OrbFlight;
            _physics.SetBodyStatus(orb, body, BodyStatus.InAir);
            _physics.SetLinearVelocity(orb, charge.LaunchVelocity, body: body);
        }

        _audio.PlayPvs(ent.Comp.ArmSound, ent);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(ent):user} deployed Corona halo ({ent.Comp.Orb})");
    }

    public void Detonate(Entity<Wh40kHaloOrbComponent> orb)
    {
        var xform = Transform(orb);
        var position = _transform.GetMapCoordinates(orb, xform);
        Spawn(orb.Comp.Blast, position);
        _audio.PlayPvs(orb.Comp.Sound, xform.Coordinates);

        var centre = position.Position;
        if (orb.Comp.Source is { } source && !TerminatingOrDeleted(source))
        {
            var from = _transform.GetMapCoordinates(source);
            if (from.MapId == position.MapId)
                centre = from.Position;
        }

        _caught.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, orb.Comp.Radius, _caught);
        foreach (var hit in _caught)
        {
            if (hit.Owner == orb.Comp.Source ||
                HasComp<Wh40kHaloOrbComponent>(hit) ||
                !_interaction.InRangeUnobstructed(orb.Owner, hit.Owner, orb.Comp.Radius + 0.5f))
            {
                continue;
            }

            if (HasComp<MobStateComponent>(hit))
            {
                if (orb.Comp.Damage is { } damage)
                    _damageable.TryChangeDamage(hit.Owner, damage, origin: orb.Comp.Source, tool: orb);

                if (orb.Comp.StaminaDamage > 0)
                    _stamina.TakeStaminaDamage(hit.Owner, orb.Comp.StaminaDamage, source: orb.Comp.Source, with: orb);
            }

            if (orb.Comp.Knockback <= 0f ||
                (hit.Comp.BodyType & (BodyType.Dynamic | BodyType.KinematicController)) == 0 ||
                Transform(hit).Anchored)
            {
                continue;
            }

            var away = _transform.GetWorldPosition(hit) - centre;
            if (away.LengthSquared() < 0.0001f)
                away = _random.NextAngle().ToVec();
            _throwing.TryThrow(hit, Vector2.Normalize(away) * orb.Comp.Knockback, orb.Comp.KnockbackSpeed,
                compensateFriction: true, playSound: false);
        }

        QueueDel(orb);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var orbs = EntityQueryEnumerator<Wh40kHaloOrbComponent, PhysicsComponent>();
        while (orbs.MoveNext(out var uid, out var orb, out var body))
        {
            if (now >= orb.DetonateAt)
            {
                if (!TerminatingOrDeleted(uid))
                    Detonate((uid, orb));
                continue;
            }

            if (orb.StopAt is not { } stop)
                continue;

            var left = now >= stop ? 0f : (float) ((stop - now) / (stop - orb.LaunchedAt));
            _physics.SetLinearVelocity(uid, orb.LaunchVelocity * left, body: body);
            if (left <= 0f)
                orb.StopAt = null;
        }

        var arming = EntityQueryEnumerator<Wh40kHaloArmingComponent>();
        while (arming.MoveNext(out var uid, out var comp))
        {
            if (now < comp.End)
                continue;

            QueueDel(comp.Shield);
            RemCompDeferred<Wh40kHaloArmingComponent>(uid);
        }

        var burst = EntityQueryEnumerator<Wh40kHaloBurstComponent>();
        while (burst.MoveNext(out var uid, out var comp))
            Recharge((uid, comp), now);

        var stun = EntityQueryEnumerator<Wh40kHaloStunComponent>();
        while (stun.MoveNext(out var uid, out var comp))
            Recharge((uid, comp), now);
    }

    private void Recharge(Entity<Wh40kBurstLauncherComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (!_actions.TryGetActionData(comp.ActionEntity, out var action, logError: false) ||
            action.Charges is not { } charges ||
            action.MaxCharges is not { } max)
        {
            return;
        }

        if (charges >= max)
            comp.NextCharge = null;
        else if (comp.NextCharge is not { } next)
            comp.NextCharge = now + comp.RechargeTime;
        else if (now >= next)
        {
            _actions.AddCharges(comp.ActionEntity, 1);
            _actions.SetEnabled(comp.ActionEntity, true);
            charges++;
            comp.NextCharge = charges < max ? now + comp.RechargeTime : null;
            _popup.PopupEntity(Loc.GetString("wh40k-burst-launcher-recharged", ("charges", charges), ("max", max)), ent, ent);
        }

        (TimeSpan Start, TimeSpan End)? wait = null;
        if (TryComp<Wh40kHaloArmingComponent>(ent, out var arming) && arming.End > now)
            wait = (arming.End - comp.Fuse, arming.End);
        else if (charges == 0 && comp.NextCharge is { } due)
            wait = (due - comp.RechargeTime, due);

        if (wait is { } w && action.Cooldown != w)
            _actions.SetCooldown(comp.ActionEntity, w.Start, w.End);
    }
}
