using System.Numerics;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;

namespace Content.Shared._WH40K.Combat;

/// <summary>
/// Shared projectile-side handling for directional barricades.
/// This keeps the component meaningful without duplicating pass/fail logic
/// across every WH40K weapon implementation.
/// </summary>
public sealed partial class SharedWH40KDirectionalBarricadeSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KDirectionalBarricadeComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<WH40KDirectionalBarricadeComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled ||
            !TryComp<ProjectileComponent>(args.OtherEntity, out var projectile))
        {
            return;
        }

        var barricadeMap = _transform.GetMapCoordinates(ent);
        var projectileMap = _transform.GetMapCoordinates(args.OtherEntity);
        if (barricadeMap.MapId == MapId.Nullspace ||
            projectileMap.MapId == MapId.Nullspace ||
            barricadeMap.MapId != projectileMap.MapId)
        {
            return;
        }

        Vector2 originDirection;
        if (projectile.Shooter is { } shooter)
        {
            var shooterMap = _transform.GetMapCoordinates(shooter);
            originDirection = shooterMap.MapId == barricadeMap.MapId
                ? shooterMap.Position - barricadeMap.Position
                : projectileMap.Position - barricadeMap.Position;
        }
        else
        {
            originDirection = projectileMap.Position - barricadeMap.Position;
        }

        var shotDirection = -originDirection;
        if (shotDirection.LengthSquared() <= 0.0001f)
            return;

        var passDirection = _transform.GetWorldRotation(ent).ToWorldVec();
        if (ent.Comp.FlipPassSide)
            passDirection = -passDirection;

        if (!WH40KDirectionalBarricadeHelpers.ShouldPassFromOrigin(
                passDirection,
                shotDirection,
                originDirection,
                ent.Comp.PassSideMaxDistance,
                ent.Comp.BlockedSidePassChance,
                ent.Comp.BlockedSidePointBlankPassDistance,
                _random))
        {
            return;
        }

        args.Cancelled = true;
    }
}
