using Content.Server.NPC.HTN;
using Content.Server._WH40K.Dialogue.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared._WH40K.Merchant;
using Content.Shared._WH40K.Npcs;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Npcs;

/// <summary>One nearby, unobstructed player may temporarily override a conversational NPC's spawn facing.</summary>
public sealed partial class Wh40kNpcFacingSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private RotateToFaceSystem _rotate = default!;
    private const float MaximumRange = 1f;
    private float _timer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DialogueInteractableComponent, MapInitEvent>(OnDialogueInit, before: [typeof(Wh40kNpcSceneSystem)]);
    }

    private void OnDialogueInit(EntityUid uid, DialogueInteractableComponent comp, MapInitEvent args) => Capture(uid);

    public void CaptureInitialized(EntityUid uid)
    {
        if (MetaData(uid).EntityLifeStage == EntityLifeStage.MapInitialized)
            Capture(uid);
    }

    public void Capture(EntityUid uid)
    {
        // Dialogue can also belong to a console or item; those are not conversational NPCs.
        if (HasComp<Wh40kNpcFacingComponent>(uid) || HasComp<ActorComponent>(uid) ||
            !(HasComp<MerchantComponent>(uid) || HasComp<HTNComponent>(uid) || HasComp<Wh40kNpcProtectionComponent>(uid)))
            return;

        var xform = Transform(uid);
        var comp = AddComp<Wh40kNpcFacingComponent>(uid);
        comp.Home = xform.Coordinates;
        comp.Facing = xform.LocalRotation;
        // A dialogue component added after seating still uses the authored scene's original direction.
        if (TryComp<Wh40kNpcSceneComponent>(uid, out var scene) && scene.Home is { } home)
        {
            comp.Home = home;
            comp.Facing = scene.Facing;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 0.2f) return;
        _timer = 0;

        var query = EntityQueryEnumerator<Wh40kNpcFacingComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (Paused(uid) || HasComp<ActorComponent>(uid) || _standing.IsDown(uid) ||
                !(HasComp<MerchantComponent>(uid) || HasComp<DialogueInteractableComponent>(uid)) ||
                !comp.Home.IsValid(EntityManager))
                continue;

            var facing = _transform.GetWorldRotation(comp.Home.EntityId) + comp.Facing;
            var range = TryComp<MerchantComponent>(uid, out var merchant)
                ? Math.Clamp(merchant.LookAtRange, 0f, MaximumRange)
                : MaximumRange;
            var point = _transform.GetMapCoordinates(uid, xform);
            var nearestDistance = range * range;
            EntityUid? nearest = null;
            foreach (var (player, actor) in _lookup.GetEntitiesInRange<ActorComponent>(point, range))
            {
                if (player == uid || actor.PlayerSession.AttachedEntity != player || !_mobState.IsAlive(player))
                    continue;
                var position = _transform.GetMapCoordinates(player);
                var delta = position.Position - point.Position;
                var distance = delta.LengthSquared();
                if (position.MapId != point.MapId || distance < 0.0001f || distance > nearestDistance ||
                    (distance == nearestDistance && nearest != null && player.Id >= nearest.Value.Id) ||
                    !_interaction.InRangeUnobstructed(uid, player, range, popup: false))
                    continue;
                nearest = player;
                nearestDistance = distance;
                facing = Angle.FromWorldVec(delta);
            }

            // Native facing turns swivel seats and refuses fixed chairs/benches.
            if (_transform.GetWorldRotation(uid) != facing)
                _rotate.TryFaceAngle(uid, facing, xform);
        }
    }
}
