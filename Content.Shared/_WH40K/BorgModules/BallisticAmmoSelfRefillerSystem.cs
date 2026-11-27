using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._WH40K.BorgModules;

/// <summary>
/// Ticks <see cref="BallisticAmmoSelfRefillerComponent"/>; refill logic lives in <see cref="SharedGunSystem"/>.
/// </summary>
public sealed partial class BallisticAmmoSelfRefillerSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BallisticAmmoProviderComponent, BallisticAmmoSelfRefillerComponent>();
        while (query.MoveNext(out var uid, out var ammo, out var refiller))
        {
            _gun.BallisticSelfRefillerUpdate((uid, ammo, refiller));
        }
    }
}
