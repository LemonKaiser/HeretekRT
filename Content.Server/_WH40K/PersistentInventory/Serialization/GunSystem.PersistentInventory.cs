using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    /// <summary>
    /// Restores only the durable fire-mode selection. Cooldown, target, holder, and current recoil
    /// are transient state for the new life and are intentionally not carried over.
    /// </summary>
    public bool TrySetPersistentInventoryFireMode(
        EntityUid uid,
        GunComponent component,
        SelectiveFire mode)
    {
        var rawMode = (byte) mode;
        if (mode == SelectiveFire.Invalid ||
            (component.AvailableModes & mode) != mode ||
            (rawMode & (rawMode - 1)) != 0)
        {
            return false;
        }

        component.SelectedMode = mode;
        Dirty(uid, component);
        return true;
    }
}
