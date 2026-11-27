using Content.Client.Movement.Systems;
using Content.Shared.Camera;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Components;

namespace Content.Client._WH40K.BorgModules;

/// <summary>
/// Offsets the camera eye toward the cursor while a <see cref="CursorOffsetInHandComponent"/> item is held.
/// </summary>
public sealed partial class CursorOffsetInHandSystem : EntitySystem
{
    [Dependency] private EyeCursorOffsetSystem _eyeOffset = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CursorOffsetInHandComponent, HeldRelayedEvent<GetEyeOffsetRelayedEvent>>(OnGetEyeOffset);
    }

    private void OnGetEyeOffset(Entity<CursorOffsetInHandComponent> ent, ref HeldRelayedEvent<GetEyeOffsetRelayedEvent> args)
    {
        if (ent.Comp.UseActiveHand &&
            (Transform(ent).ParentUid is not { Valid: true } holder || _hands.GetActiveItem(holder) != ent.Owner))
            return;

        if (_eyeOffset.OffsetAfterMouse(ent.Owner, null) is { } offset)
            args.Args.Offset += offset;
    }
}
