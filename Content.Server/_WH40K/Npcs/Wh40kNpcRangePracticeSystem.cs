using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Server._WH40K.Dialogue;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared._WH40K.Npcs;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Npcs;

/// <summary>Request native shots from the authored training gun; never modify ammunition or firing limits.</summary>
public sealed class Wh40kNpcRangePracticeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly DialogueSystem _dialogue = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    private static readonly EntProtoId TrainingGun = "WeaponHeavyStubPistolArbitesPractice";
    private static readonly EntProtoId TrainingBullet = "BulletArbitesPractice";
    private float _scanTime;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kNpcRangePracticeComponent, MapInitEvent>(OnInit);
    }

    private void OnInit(EntityUid uid, Wh40kNpcRangePracticeComponent comp, MapInitEvent args)
        => comp.Accumulator = MathF.Max(0, comp.Interval - 5);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _scanTime += frameTime;
        if (_scanTime < 1) return;
        var elapsed = _scanTime;
        _scanTime = 0;

        if (_ticker.RunLevel != GameRunLevel.InRound || !_config.GetCVar(CCVars.Wh40kAmbientNpcsEnabled))
            return;

        var query = EntityQueryEnumerator<Wh40kNpcRangePracticeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (Paused(uid) || !_mobState.IsAlive(uid) || !HasComp<ActiveNPCComponent>(uid) ||
                !TryComp<HTNComponent>(uid, out var htn) || !htn.Enabled ||
                !float.IsFinite(comp.Interval) || comp.Interval < 5 || comp.Interval > 3600 ||
                !float.IsFinite(comp.PlayerCheckRange) || comp.PlayerCheckRange <= 0 || comp.PlayerCheckRange > 16 ||
                !float.IsFinite(comp.TargetDistance) || comp.TargetDistance <= 0 || comp.TargetDistance > 16 ||
                !HasAudience(uid, comp, xform))
                continue;

            comp.Accumulator += elapsed;
            if (comp.Accumulator < comp.Interval) continue;
            comp.Accumulator = 0;
            Shoot(uid, comp, xform);
        }
    }

    private bool HasAudience(EntityUid uid, Wh40kNpcRangePracticeComponent comp, TransformComponent xform)
    {
        var nearby = false;
        var point = _transform.GetMapCoordinates(uid, xform);
        foreach (var (player, actor) in _lookup.GetEntitiesInRange<ActorComponent>(point, comp.PlayerCheckRange))
        {
            if (_dialogue.IsInDialogue(actor.PlayerSession)) return false;
            if (_mobState.IsAlive(player)) nearby = true;
        }
        return nearby;
    }

    private void Shoot(EntityUid uid, Wh40kNpcRangePracticeComponent comp, TransformComponent xform)
    {
        // Only this dedicated zero-damage weapon may be used for automatic practice.
        if (!_gun.TryGetGun(uid, out var gunUid, out var gun) ||
            MetaData(gunUid).EntityPrototype?.ID != TrainingGun.Id ||
            !TryComp<BasicEntityAmmoProviderComponent>(gunUid, out var ammo) || ammo.Proto != TrainingBullet.Id ||
            gun.NextFire > _timing.CurTime || !TryComp<Wh40kNpcSceneComponent>(uid, out var scene) ||
            scene.Home is not { } home || !home.IsValid(EntityManager))
            return;

        // Preserve the mapper's direction rather than turning a visitor into a target.
        var facing = _transform.GetWorldRotation(home.EntityId) + scene.Facing;
        _transform.SetWorldRotation(uid, facing);
        var point = _transform.GetMapCoordinates(uid, xform);
        var target = new MapCoordinates(point.Position + facing.ToWorldVec() * comp.TargetDistance, point.MapId);
        _gun.AttemptShoot(uid, gunUid, gun, _transform.ToCoordinates(target));
    }
}
