using Content.Shared.Durability.Components;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared.Durability.Systems;

/// <summary>
/// Shared helpers for durability-aware content. State mutation is implemented by the server system.
/// </summary>
public abstract partial class SharedItemDurabilitySystem : EntitySystem
{
    [Dependency] protected readonly SharedPopupSystem Popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemDurabilityComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnShotAttempted(Entity<ItemDurabilityComponent> ent, ref ShotAttemptedEvent args)
    {
        if (IsUsable(ent.Owner, ent.Comp))
            return;

        args.Cancel();
        Popup.PopupClient(Loc.GetString("durability-item-broken"), ent.Owner, args.User);
    }

    public bool IsUsable(EntityUid uid, ItemDurabilityComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return true;

        return !component.Broken && component.CurrentDurability > 0f;
    }

    public static bool CoversBodyPart(ItemDurabilityComponent component, Content.Shared._Shitmed.Targeting.TargetBodyPart? targetPart)
    {
        if (!component.ProtectsWearer || component.ProtectedBodyParts == 0)
            return false;

        if (!targetPart.HasValue || targetPart.Value == Content.Shared._Shitmed.Targeting.TargetBodyPart.All)
            return true;

        return (component.ProtectedBodyParts & targetPart.Value) != 0;
    }
}
