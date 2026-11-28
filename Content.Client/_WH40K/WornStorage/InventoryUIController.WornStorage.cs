using System.Numerics;
using Content.Client.Interaction;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Hands.Controls;
using Content.Shared._WH40K.WornStorage;
using Content.Shared.Storage;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client.UserInterface.Systems.Inventory;

public sealed partial class InventoryUIController
{
    [Dependency] private IConfigurationManager _wh40kCfg = default!;

    private DragDropHelper<SlotControl>? _wornStorageDrag;
    private SpriteView? _wornStorageGhost;

    private bool TryHoldWornStorageClick(GUIBoundKeyEventArgs args, SlotControl control)
    {
        if (!_wh40kCfg.GetCVar(Wh40kWornStorageCVars.ClickOpens) || control is HandButton || _playerUid is not { } player)
            return false;

        if (!_inventorySystem.TryGetSlotEntity(player, control.SlotName, out var item)
            || !_entities.HasComponent<StorageComponent>(item))
            return false;

        _wornStorageDrag ??= new DragDropHelper<SlotControl>(OnWornStorageBeginDrag, OnWornStorageContinueDrag, OnWornStorageEndDrag);
        _wornStorageDrag.MouseDown(control);
        args.Handle();
        return true;
    }

    private void OnWornStorageSlotUnpressed(GUIBoundKeyEventArgs args, SlotControl control)
    {
        if (args.Function != EngineKeyFunctions.UIClick || _wornStorageDrag?.Dragged != control)
            return;

        var dragged = _wornStorageDrag.IsDragging;
        _wornStorageDrag.EndDrag();

        if (!dragged)
        {
            if (_handsSystem.GetActiveHandEntity() == null)
                _inventorySystem.UIInventoryStorageActivate(control.SlotName);
            else
                _inventorySystem.UIInventoryActivate(control.SlotName);
            return;
        }

        if (FindHandButton(UIManager.MouseGetControl(args.PointerLocation)) is { } handButton)
            DropWornStorageOnHand(control.SlotName, handButton.SlotName);
    }

    public void DropWornStorageOnHand(string slot, string handName)
    {
        if (_playerUid is not { } player
            || !_handsSystem.TryGetPlayerHands(out var hands)
            || !_handsSystem.TryGetHand(player, handName, out var hand, hands)
            || hand.HeldEntity != null)
            return;

        if (hands.ActiveHand != hand)
            _handsSystem.UIHandClick(hands, hand.Name);

        _inventorySystem.UIInventoryActivate(slot);
    }

    private static HandButton? FindHandButton(Control? control)
    {
        for (var current = control; current != null; current = current.Parent)
        {
            if (current is HandButton hand)
                return hand;
        }

        return null;
    }

    private bool OnWornStorageBeginDrag()
    {
        if (_wornStorageDrag?.Dragged?.Entity is not { } entity)
            return false;

        _wornStorageGhost = new SpriteView
        {
            Scale = new Vector2(2, 2),
            SetSize = new Vector2(SlotControl.DefaultButtonSize, SlotControl.DefaultButtonSize),
            OverrideDirection = Direction.South,
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
        _wornStorageGhost.SetEntity(entity);
        UIManager.PopupRoot.AddChild(_wornStorageGhost);
        PositionWornStorageGhost();
        return true;
    }

    private bool OnWornStorageContinueDrag(float frameTime)
    {
        PositionWornStorageGhost();
        return true;
    }

    private void OnWornStorageEndDrag()
    {
        _wornStorageGhost?.Orphan();
        _wornStorageGhost = null;
    }

    private void PositionWornStorageGhost()
    {
        if (_wornStorageGhost == null)
            return;

        var half = new Vector2(SlotControl.DefaultButtonSize, SlotControl.DefaultButtonSize) / 2;
        LayoutContainer.SetPosition(_wornStorageGhost, UIManager.MousePositionScaled.Position - half);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _wornStorageDrag?.Update(args.DeltaSeconds);
    }
}
