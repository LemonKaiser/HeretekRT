using Content.Server.Physics.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    private void InitializeTargetAssign()
    {
        SubscribeLocalEvent<TargetAssignComponent, AmmoShotEvent>(OnTargetAssignShot);
    }

    private void OnTargetAssignShot(EntityUid uid, TargetAssignComponent comp, AmmoShotEvent args)
    {
        if (comp.Target is not { } target || TerminatingOrDeleted(target))
            return;

        foreach (var projectile in args.FiredProjectiles)
        {
            if (!TryComp<ChasingWalkComponent>(projectile, out var chasing))
                continue;

            chasing.ChasingEntity = target;
            if (chasing.Speed <= 0f)
                chasing.Speed = chasing.MaxSpeed;

            EnsureComp<TargetedProjectileComponent>(projectile).Target = target;
        }
    }
}
