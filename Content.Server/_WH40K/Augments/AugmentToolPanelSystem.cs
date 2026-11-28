using Content.Server._WH40K.Localizations;
using Content.Shared._WH40K.Augments;
using Content.Shared.Body.Part;
using Content.Shared.Emp;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments;

/// <summary>
/// Presents tools stored in an arm implant and temporarily places the chosen one in the matching hand.
/// Supports Q-retract, active-tool tracking, and EMP retraction + temporary lockout.
/// </summary>
public sealed partial class AugmentToolPanelSystem : SharedAugmentToolPanelSystem
{
    [Dependency] private AugmentPowerCellSystem _power = default!;
    [Dependency] private AugmentSystem _augment = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popups = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private WH40KPlayerCultureManager _playerCulture = default!;

    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<BodyPartComponent> _partQuery;

    public override void Initialize()
    {
        base.Initialize();
        _handsQuery = GetEntityQuery<HandsComponent>();
        _partQuery = GetEntityQuery<BodyPartComponent>();

        SubscribeLocalEvent<AugmentToolPanelComponent, AugmentLostPowerEvent>(OnLostPower);
        SubscribeLocalEvent<AugmentToolPanelComponent, EmpPulseEvent>(OnOrganEmpPulse);
        Subs.BuiEvents<AugmentToolPanelComponent>(AugmentToolPanelUiKey.Key, subs =>
        {
            subs.Event<AugmentToolPanelSwitchMessage>(OnSwitchTool);
        });
    }

    private void OnLostPower(Entity<AugmentToolPanelComponent> augment, ref AugmentLostPowerEvent args)
    {
        SwitchTool(augment, null, args.Body);
    }

    private void OnOrganEmpPulse(Entity<AugmentToolPanelComponent> augment, ref EmpPulseEvent args)
    {
        var lockout = args.Duration > TimeSpan.FromSeconds(1)
            ? args.Duration
            : TimeSpan.FromSeconds(1);
        var disabledUntil = _timing.CurTime + lockout;
        if (disabledUntil > augment.Comp.EmpDisabledUntil)
        {
            augment.Comp.EmpDisabledUntil = disabledUntil;
            Dirty(augment);
        }

        if (_augment.GetBody(augment) is { } body)
        {
            SwitchTool(augment, null, body);
            args.Affected = true;
        }
    }

    /// <summary>
    /// Retracts an active tool-panel item held in the wearer's hand back into its owning panel organ.
    /// </summary>
    public bool TryRetractActiveItem(EntityUid body, EntityUid item)
    {
        if (!HasComp<AugmentToolPanelActiveItemComponent>(item) ||
            !TryComp<InstalledAugmentsComponent>(body, out var installed))
        {
            return false;
        }

        var itemNet = GetNetEntity(item);
        foreach (var augmentNet in installed.InstalledAugments)
        {
            var augmentUid = GetEntity(augmentNet);
            if (!TryComp<AugmentToolPanelComponent>(augmentUid, out var panel))
                continue;

            if (panel.ActiveTool != null && panel.ActiveTool != itemNet)
                continue;

            if (!TryGetMatchingHand(augmentUid, body, out var hand) ||
                !_hands.TryGetHand(body, hand, out var heldHand) ||
                heldHand.HeldEntity != item)
            {
                continue;
            }

            SwitchTool((augmentUid, panel), null, body, hand);
            return true;
        }

        return false;
    }

    private void OnSwitchTool(Entity<AugmentToolPanelComponent> augment, ref AugmentToolPanelSwitchMessage args)
    {
        if (_augment.GetBody(augment) is not { } body)
            return;

        EntityUid? desiredTool = null;
        if (args.DesiredTool is { } desiredToolNet)
        {
            if (_timing.CurTime < augment.Comp.EmpDisabledUntil)
            {
                _popups.PopupEntity(
                    _playerCulture.GetPlayerString(body, "augment-tool-panel-emp-locked"),
                    body,
                    body,
                    PopupType.MediumCaution);
                return;
            }

            if (augment.Comp.ActiveTool == desiredToolNet)
            {
                // Clicking the already-deployed tool retracts it cleanly without spending switch charge.
                SwitchTool(augment, null, body);
                return;
            }

            if (!TryGetEntity(desiredToolNet, out var resolvedTool) ||
                resolvedTool is not { } tool ||
                !TryComp<StorageComponent>(augment, out var storage) ||
                !storage.StoredItems.ContainsKey(tool))
            {
                return;
            }

            desiredTool = tool;
        }

        if (desiredTool != null && !_power.TryUseChargeBody(body, augment.Comp.SwitchCharge))
            return;

        SwitchTool(augment, desiredTool, body);
    }

    public void SwitchTool(Entity<AugmentToolPanelComponent> augment, EntityUid? tool, EntityUid body)
    {
        if (!TryGetMatchingHand(augment, body, out var hand))
        {
            if (_handsQuery.HasComp(body))
                _popups.PopupEntity(_playerCulture.GetPlayerString(body, "augment-tool-panel-no-hand"), body, body, PopupType.LargeCaution);
            return;
        }

        SwitchTool(augment, tool, body, hand);
    }

    private bool TryGetMatchingHand(EntityUid augment, EntityUid body, out string matchedHand)
    {
        matchedHand = string.Empty;
        if (!_handsQuery.TryComp(body, out var hands))
            return false;

        var arm = Transform(augment).ParentUid;
        if (!_partQuery.TryComp(arm, out var part))
            return false;

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
                matchedHand = hand;
                return true;
            }
        }

        return false;
    }

    private void SwitchTool(Entity<AugmentToolPanelComponent> augment, EntityUid? desiredTool, EntityUid body, string hand)
    {
        if (_hands.TryGetHand(body, hand, out var heldHand) && heldHand.HeldEntity is { } item)
        {
            if (!RemComp<AugmentToolPanelActiveItemComponent>(item))
            {
                _popups.PopupEntity(_playerCulture.GetPlayerString(body, "augment-tool-panel-hand-full"), body, body, PopupType.SmallCaution);
                return;
            }

            if (!TryComp<StorageComponent>(augment, out var storage) ||
                !_storage.PlayerInsertEntityInWorld((augment.Owner, storage), body, item))
            {
                EnsureComp<AugmentToolPanelActiveItemComponent>(item);
                return;
            }

            if (augment.Comp.ActiveTool != null)
            {
                augment.Comp.ActiveTool = null;
                Dirty(augment);
            }

            if (desiredTool == null)
                _popups.PopupEntity(_playerCulture.GetPlayerString(body, "augment-tool-panel-retracted", ("item", item)), body, body);

            _toggle.TryDeactivate(augment.Owner, user: body);
        }
        else if (augment.Comp.ActiveTool != null)
        {
            augment.Comp.ActiveTool = null;
            Dirty(augment);
        }

        if (desiredTool is not { } tool)
            return;

        if (!_hands.TryPickup(body, tool, hand))
        {
            _popups.PopupEntity(_playerCulture.GetPlayerString(body, "augment-tool-panel-cannot-pick-up"), body, body, PopupType.SmallCaution);
            return;
        }

        EnsureComp<AugmentToolPanelActiveItemComponent>(tool);
        augment.Comp.ActiveTool = GetNetEntity(tool);
        Dirty(augment);
        _toggle.TryActivate(augment.Owner, user: body);
        _popups.PopupEntity(_playerCulture.GetPlayerString(body, "augment-tool-panel-selected", ("item", tool)), body, body);
    }
}
