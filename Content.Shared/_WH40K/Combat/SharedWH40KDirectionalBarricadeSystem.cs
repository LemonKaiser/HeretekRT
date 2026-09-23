using System.Numerics;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Shared._WH40K.Combat;

/// <summary>
/// Directional cover for projectiles and hitscan traces.
/// </summary>
public sealed partial class SharedWH40KDirectionalBarricadeSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, (EntityUid? Shooter, MapCoordinates? Origin, Dictionary<EntityUid, bool> Results)> _projectileResults = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KDirectionalBarricadeComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<ProjectileComponent, ComponentShutdown>(OnProjectileShutdown);
    }

    private void OnProjectileShutdown(Entity<ProjectileComponent> ent, ref ComponentShutdown args)
    {
        _projectileResults.Remove(ent.Owner);
    }

    private void OnPreventCollide(Entity<WH40KDirectionalBarricadeComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled || !TryComp<ProjectileComponent>(args.OtherEntity, out var projectile))
            return;

        var projectileUid = args.OtherEntity;
        if (!_projectileResults.TryGetValue(projectileUid, out var cached) ||
            cached.Shooter != projectile.Shooter || cached.Origin != projectile.ShotOrigin)
        {
            cached = (projectile.Shooter, projectile.ShotOrigin, new Dictionary<EntityUid, bool>());
            _projectileResults[projectileUid] = cached;
        }

        if (!cached.Results.TryGetValue(ent.Owner, out var pass))
        {
            var barricadeMap = _transform.GetMapCoordinates(ent);
            var projectileMap = _transform.GetMapCoordinates(projectileUid);
            if (barricadeMap.MapId == MapId.Nullspace || projectileMap.MapId != barricadeMap.MapId)
                return;

            var origin = projectile.ShotOrigin is { } shotOrigin && shotOrigin.MapId == barricadeMap.MapId
                ? shotOrigin.Position
                : projectileMap.Position;
            if (projectile.ShotOrigin == null && projectile.Shooter is { } shooter && Exists(shooter))
            {
                var shooterMap = _transform.GetMapCoordinates(shooter);
                if (shooterMap.MapId == barricadeMap.MapId)
                    origin = shooterMap.Position;
            }
            var velocity = projectile.RaycastResetVelocity ?? _physics.GetMapLinearVelocity(projectileUid);
            pass = ShouldPass(ent, barricadeMap, origin, velocity);
            cached.Results[ent.Owner] = pass;
        }

        if (pass)
            args.Cancelled = true;
    }

    public bool ShouldPassHitscan(EntityUid barricade, MapCoordinates origin, Vector2 shotDirection)
    {
        if (!TryComp<WH40KDirectionalBarricadeComponent>(barricade, out var component))
            return false;

        var barricadeMap = _transform.GetMapCoordinates(barricade);
        return barricadeMap.MapId == origin.MapId &&
               ShouldPass((barricade, component), barricadeMap, origin.Position, shotDirection);
    }

    private bool ShouldPass(Entity<WH40KDirectionalBarricadeComponent> barricade,
        MapCoordinates barricadeMap,
        Vector2 origin,
        Vector2 shotDirection)
    {
        var passDirection = _transform.GetWorldRotation(barricade).ToWorldVec();
        if (barricade.Comp.FlipPassSide)
            passDirection = -passDirection;

        return WH40KDirectionalBarricadeHelpers.ShouldPassFromOrigin(
            passDirection,
            shotDirection,
            origin - barricadeMap.Position,
            barricade.Comp.PassSideMaxDistance,
            barricade.Comp.BlockedSidePassChance,
            barricade.Comp.BlockedSidePointBlankPassDistance,
            _random);
    }
}
