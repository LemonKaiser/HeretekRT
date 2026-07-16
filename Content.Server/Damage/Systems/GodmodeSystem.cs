using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;

namespace Content.Server.Damage.Systems;

public sealed class GodmodeSystem : SharedGodmodeSystem
{
    /// <summary>
    /// Restores runtime bookkeeping after a save hook temporarily removes a Godmode component.
    /// </summary>
    public void RestoreGodmodeState(EntityUid uid, bool wasMovedByPressure, DamageSpecifier? oldDamage)
    {
        if (!TryComp<GodmodeComponent>(uid, out var godmode))
            return;

        godmode.WasMovedByPressure = wasMovedByPressure;
        godmode.OldDamage = oldDamage;
    }

    public override void EnableGodmode(EntityUid uid, GodmodeComponent? godmode = null)
    {
        godmode ??= EnsureComp<GodmodeComponent>(uid);

        base.EnableGodmode(uid, godmode);

        if (TryComp<MovedByPressureComponent>(uid, out var moved))
        {
            godmode.WasMovedByPressure = moved.Enabled;
            moved.Enabled = false;
        }
    }

    public override void DisableGodmode(EntityUid uid, GodmodeComponent? godmode = null)
    {
    	if (!Resolve(uid, ref godmode, false))
    	    return;

        base.DisableGodmode(uid, godmode);

        if (godmode.Deleted)
            return;

        if (TryComp<MovedByPressureComponent>(uid, out var moved))
        {
            moved.Enabled = godmode.WasMovedByPressure;
        }
    }
}
