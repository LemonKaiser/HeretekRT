using System.Linq;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Administration.Logs;
using Content.Shared._WH40K.Combat;
using Content.Shared._WH40K.NPC;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicRaycastSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ISharedAdminLogManager _log = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedWH40KDirectionalBarricadeSystem _barricades = default!;
    [Dependency] private Wh40kChaosFriendlyFireSystem _chaosFriendlyFire = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanBasicRaycastComponent, HitscanTraceEvent>(OnHitscanFired);
    }

    private void OnHitscanFired(Entity<HitscanBasicRaycastComponent> ent, ref HitscanTraceEvent args)
    {
        var shooter = args.Shooter ?? args.Gun;
        var mapCords = _transform.ToMapCoordinates(args.FromCoordinates);
        var ray = new CollisionRay(mapCords.Position, args.ShotDirection, (int) ent.Comp.CollisionMask);
        var rayCastResults = _physics.IntersectRay(mapCords.MapId, ray, ent.Comp.MaxDistance, shooter, false);

        var target = args.Target;
        var gunGrid = Transform(args.Gun).GridUid;
        var shotDirection = args.ShotDirection;
        var inContainer = _container.IsEntityOrParentInContainer(shooter);
        var result = rayCastResults.FirstOrNull(hit =>
            !_chaosFriendlyFire.ShouldPass(shooter, hit.HitEntity) &&
            !_barricades.ShouldPassHitscan(hit.HitEntity, mapCords, shotDirection) &&
            (inContainer || hit.HitEntity == target ||
             CompOrNull<RequireProjectileTargetComponent>(hit.HitEntity)?.Active != true) &&
            (!TryComp<ShipShieldComponent>(hit.HitEntity, out var shield) ||
             (!ent.Comp.IgnoreShields && gunGrid != shield.Shielded)));

        var trace = new HitscanRaycastFiredEvent
        {
            FromCoordinates = args.FromCoordinates,
            ShotDirection = args.ShotDirection,
            Gun = args.Gun,
            Shooter = args.Shooter,
            HitEntities = [], // Mono
            DistanceTried = result?.Distance ?? ent.Comp.MaxDistance,
        };

        if (result?.HitEntity != null) // Mono
        {
            trace.HitEntities.Add(result.Value.HitEntity);

            _log.Add(LogType.HitScanHit,
                $"{ToPrettyString(shooter):user} hit {ToPrettyString(result.Value.HitEntity):target}"
                + $" using {ToPrettyString(args.Gun):entity}.");
        }

        RaiseLocalEvent(ent, ref trace);
    }
}
