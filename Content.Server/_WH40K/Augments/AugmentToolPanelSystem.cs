using Content.Shared.Body.Part;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared._WH40K.Augments;

namespace Content.Server._WH40K.Augments;

/// <summary>
/// Presents tools stored in an arm implant and temporarily places the chosen one in the matching hand.
/// </summary>
public sealed partial class AugmentToolPanelSystem : SharedAugmentToolPanelSystem
{
    [Dependency] private AugmentPowerCellSystem _power = default!;
    [Dependency] private AugmentSystem _augment = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popups = default!;
    [Dependency] private SharedStorageSystem _storage = default!;

    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<BodyPartComponent> _partQuery;

    public override void Initialize()
    {
        base.Initialize();
        _handsQuery = GetEntityQuery<HandsComponent>();
        _partQuery = GetEntityQuery<BodyPartComponent>();

        SubscribeLocalEvent<AugmentToolPanelComponent, AugmentLostPowerEvent>(OnLostPower);
        Subs.BuiEvents<AugmentToolPanelComponent>(AugmentToolPanelUiKey.Key, subs =>
        {
            subs.Event<AugmentToolPanelSwitchMessage>(OnSwitchTool);
        });
    }

    private void OnLostPower(Entity<AugmentToolPanelComponent> augment, ref AugmentLostPowerEvent args)
    {
        SwitchTool(augment, null, args.Body);
    }

    private void OnSwitchTool(Entity<AugmentToolPanelComponent> augment, ref AugmentToolPanelSwitchMessage args)
    {
        EntityUid? desiredTool = null;
        if (args.DesiredTool is { } desiredToolNet)
        {
            if (!TryGetEntity(desiredToolNet, out var resolvedTool) ||
                resolvedTool is not { } tool ||
                !TryComp<StorageComponent>(augment, out var storage) ||
                !storage.StoredItems.ContainsKey(tool))
            {
                return;
            }

            desiredTool = tool;
        }

        if (_augment.GetBody(augment) is not { } body || !_power.TryUseChargeBody(body, augment.Comp.SwitchCharge))
            return;

        SwitchTool(augment, desiredTool, body);
    }

    public void SwitchTool(Entity<AugmentToolPanelComponent> augment, EntityUid? tool, EntityUid body)
    {
        if (!_handsQuery.TryComp(body, out var hands))
            return;

        var arm = Transform(augment).ParentUid;
        if (!_partQuery.TryComp(arm, out var part))
            return;

        var location = part.Symmetry switch
        {
            BodyPartSymmetry.Left => HandLocation.Left,
            BodyPartSymmetry.Right => HandLocation.Right,
            _ => HandLocation.Middle,
        };

        foreach (var (hand, data) in hands.Hands)
        {
            if (data.Location == location)
            {
                SwitchTool(augment, tool, body, hand);
                return;
            }
        }

        _popups.PopupEntity(Loc.GetString("augment-tool-panel-no-hand"), body, body, PopupType.LargeCaution);
    }

    private void SwitchTool(Entity<AugmentToolPanelComponent> augment, EntityUid? desiredTool, EntityUid body, string hand)
    {
        if (_hands.TryGetHand(body, hand, out var heldHand) && heldHand.HeldEntity is { } item)
        {
            if (!RemComp<AugmentToolPanelActiveItemComponent>(item))
            {
                _popups.PopupEntity(Loc.GetString("augment-tool-panel-hand-full"), body, body, PopupType.SmallCaution);
                return;
            }

            if (!TryComp<StorageComponent>(augment, out var storage) ||
                !_storage.PlayerInsertEntityInWorld((augment.Owner, storage), body, item))
            {
                EnsureComp<AugmentToolPanelActiveItemComponent>(item);
                return;
            }

            if (desiredTool == null)
                _popups.PopupEntity(Loc.GetString("augment-tool-panel-retracted", ("item", item)), body, body);

            _toggle.TryDeactivate(augment.Owner, user: body);
        }

        if (desiredTool is not { } tool)
            return;

        if (!_hands.TryPickup(body, tool, hand))
        {
            _popups.PopupEntity(Loc.GetString("augment-tool-panel-cannot-pick-up"), body, body, PopupType.SmallCaution);
            return;
        }

        EnsureComp<AugmentToolPanelActiveItemComponent>(tool);
        _toggle.TryActivate(augment.Owner, user: body);
        _popups.PopupEntity(Loc.GetString("augment-tool-panel-selected", ("item", tool)), body, body);
    }
}
