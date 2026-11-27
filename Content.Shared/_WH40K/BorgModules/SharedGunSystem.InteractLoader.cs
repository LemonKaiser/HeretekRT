using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    [Dependency] private SharedStackSystem _wh40kStack = default!;

    private void InitializeInteractLoader()
    {
        SubscribeLocalEvent<BallisticAmmoInteractLoaderComponent, AfterInteractEvent>(OnBallisticAmmoLoad);
    }

    private void OnBallisticAmmoLoad(EntityUid uid, BallisticAmmoInteractLoaderComponent comp, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !_netManager.IsServer)
            return;

        if (!TryComp<BallisticAmmoProviderComponent>(uid, out var ammo) ||
            GetBallisticShots(ammo) >= ammo.Capacity ||
            (_whitelistSystem.IsWhitelistFailOrNull(ammo.Whitelist, target) && !ammo.IgnoreWhitelist))
            return;

        var inserted = target;
        if (TryComp<StackComponent>(target, out var stack) && stack.Count > 1)
        {
            if (_wh40kStack.Split(target, 1, Transform(target).Coordinates, stack) is not { } split)
                return;

            inserted = split;
        }

        ammo.Entities.Add(inserted);
        Containers.Insert(inserted, ammo.Container);
        Audio.PlayPvs(ammo.SoundInsert, uid);
        UpdateBallisticAppearance(uid, ammo);
        UpdateAmmoCount(uid);
        DirtyField(uid, ammo, nameof(BallisticAmmoProviderComponent.Entities));
        args.Handled = true;
    }
}
