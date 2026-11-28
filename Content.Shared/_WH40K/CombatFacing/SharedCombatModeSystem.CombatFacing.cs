using Content.Shared._WH40K.CombatFacing;

namespace Content.Shared.CombatMode;

public abstract partial class SharedCombatModeSystem
{
    [Dependency] private Wh40kCombatFacingSystem _wh40kCombatFacing = default!;

    private bool Wh40kFacesCursor(EntityUid uid)
    {
        return _wh40kCombatFacing.WantsFaceCursor(uid);
    }

    public void Wh40kSetFaceCursor(EntityUid uid, bool faceCursor)
    {
        if (!TryComp<CombatModeComponent>(uid, out var combat) || !combat.IsInCombatMode || !combat.ToggleMouseRotator)
            return;

        SetMouseRotatorComponents(uid, faceCursor);
    }
}
