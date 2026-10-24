using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    public void SetPersistentCartridgeSpent(EntityUid uid, CartridgeAmmoComponent cartridge, bool spent)
    {
        // SetCartridgeSpent also starts a fresh despawn timer, which is not a restored cartridge state.
        cartridge.Spent = spent;
        Appearance.SetData(uid, AmmoVisuals.Spent, spent);
        Dirty(uid, cartridge);
    }

    public bool TrySetPersistentRevolverState(EntityUid uid, RevolverAmmoProviderComponent ammo,
        int current, bool?[] chambers, int[] indices)
    {
        var contents = ammo.AmmoContainer.ContainedEntities;
        if (ammo.Capacity <= 0 || current < 0 || current >= ammo.Capacity ||
            chambers.Length != ammo.Capacity || indices.Length != ammo.Capacity)
            return false;

        var used = new HashSet<int>();
        var slots = new List<EntityUid?>();
        for (var i = 0; i < ammo.Capacity; i++)
        {
            var index = indices[i];
            if (index < -1 || index >= contents.Count || index >= 0 && !used.Add(index) ||
                index == -1 && chambers[i] != null && ammo.FillPrototype == null)
                return false;
            var item = index < 0 ? (EntityUid?) null : contents[index];
            // Physical cartridges carry their own spent state. The chamber hint can lag behind it after firing.
            slots.Add(item);
        }
        if (used.Count != contents.Count)
            return false;

        ammo.CurrentIndex = current;
        ammo.AmmoSlots = slots;
        ammo.Chambers = chambers;
        UpdateRevolverAppearance(uid, ammo);
        UpdateAmmoCount(uid);
        Dirty(uid, ammo);
        return true;
    }
}
