using Content.Server.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Shrapnel;

/// <summary>
/// Fires shrapnel projectiles outward for <see cref="Wh40kShrapnelBehavior"/> while preserving moving grid velocity.
/// </summary>
public sealed partial class Wh40kShrapnelSystem : EntitySystem
{
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public void Fling(EntityUid uid, Wh40kShrapnelBehavior behavior)
    {
        var origin = _transform.GetMapCoordinates(uid);
        if (origin.MapId == MapId.Nullspace || behavior.Count <= 0)
            return;

        var velocity = _physics.GetMapLinearVelocity(uid);
        var slice = 360f / behavior.Count;

        for (var i = 0; i < behavior.Count; i++)
        {
            var direction = Angle.FromDegrees(slice * (i + _random.NextFloat())).ToVec();
            var shrapnel = Spawn(behavior.Proto, origin.Offset(direction * behavior.SpawnRadius));

            var gunVelocity = velocity - _physics.GetMapLinearVelocity(shrapnel)
                + _random.NextVector2(behavior.MinJitter, behavior.MaxJitter);

            _gun.ShootProjectile(shrapnel, direction, gunVelocity, uid, null, behavior.Speed);
        }
    }
}
