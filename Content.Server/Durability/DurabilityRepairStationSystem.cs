using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Durability;
using Content.Shared.Durability.Components;
using Content.Shared.Durability.Events;
using Content.Shared.DoAfter;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Stacks;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;

namespace Content.Server.Durability;

/// <summary>
/// Handles material-based durability repairs on a workbench's single internal item slot.
/// The slot shows its contents visually while the contained item has no world physics or collision.
/// </summary>
public sealed partial class DurabilityRepairStationSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ItemDurabilitySystem _durability = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DurabilityRepairStationComponent, ComponentInit>(OnComponentInit,
            after: new[] { typeof(ItemSlotsSystem) });
        SubscribeLocalEvent<DurabilityRepairStationComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<DurabilityRepairStationComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<DurabilityRepairStationComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<DurabilityRepairStationComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        SubscribeLocalEvent<DurabilityRepairStationComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<DurabilityRepairStationComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<DurabilityRepairStationComponent, DurabilityRepairDoAfterEvent>(OnRepairDoAfter);
        SubscribeLocalEvent<DurabilityRepairStationComponent, ExaminedEvent>(OnExamined);
    }

    private void OnComponentInit(Entity<DurabilityRepairStationComponent> ent, ref ComponentInit args)
    {
        ent.Comp.MaxRepairSlots = 1;
        ConfigureRepairSlot(ent);
    }

    private void OnMapInit(Entity<DurabilityRepairStationComponent> ent, ref MapInitEvent args)
    {
        var component = ent.Comp;
        if (component.Tier is < 1 or > 3)
        {
            Log.Error($"Entity {ToPrettyString(ent)} has invalid durability repair station tier {component.Tier}.");
            RemCompDeferred<DurabilityRepairStationComponent>(ent);
            return;
        }

        if (component.RepairPerMaterial <= 0f || !float.IsFinite(component.RepairPerMaterial))
            component.RepairPerMaterial = component.Tier switch
            {
                1 => 10f,
                2 => 15f,
                _ => 20f,
            };

        if (component.RepairDurationSeconds <= 0f || !float.IsFinite(component.RepairDurationSeconds))
            component.RepairDurationSeconds = component.Tier switch
            {
                1 => 1f,
                2 => 0.5f,
                _ => 0.25f,
            };

        if (component.Tier >= 3)
            component.RequirePower = true;

        ConfigureRepairSlot(ent);
        Dirty(ent);
    }

    private void OnComponentShutdown(Entity<DurabilityRepairStationComponent> ent, ref ComponentShutdown args)
    {
        CancelActive(ent);
    }

    private void OnInteractUsing(Entity<DurabilityRepairStationComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<ToolComponent>(args.Used))
            return;

        // Any tool is an explicit repair attempt. Do not let the repair slot consume it.
        args.Handled = true;
        TryStartRepair(ent, args.User, args.Used);
    }

    private void OnItemRemoved(Entity<DurabilityRepairStationComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == DurabilityRepairStationComponent.RepairSlotId && ent.Comp.ActiveItem == args.Entity)
            CancelActive(ent);
    }

    private void OnAnchorChanged(Entity<DurabilityRepairStationComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            CancelActive(ent);
    }

    private void OnPowerChanged(Entity<DurabilityRepairStationComponent> ent, ref PowerChangedEvent args)
    {
        if (ent.Comp.RequirePower && !args.Powered)
            CancelActive(ent);
    }

    private void OnExamined(Entity<DurabilityRepairStationComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange ||
            _itemSlots.GetItemOrNull(ent, DurabilityRepairStationComponent.RepairSlotId) is not { } item ||
            !TryComp(item, out ItemDurabilityComponent? durability) ||
            durability.RepairMaterial is not { } repairMaterial)
        {
            return;
        }

        var requiredTool = GetRequiredToolQuality(ent.Comp.Tier);
        var materialName = GetStackName(repairMaterial);
        var toolName = GetToolName(requiredTool);
        var itemName = MetaData(item).EntityName;

        args.PushMarkup(Loc.GetString("durability-repair-station-examine",
            ("item", itemName),
            ("tier", durability.RequiredWorkbenchTier),
            ("material", materialName),
            ("tool", toolName)));
    }

    private void OnRepairDoAfter(Entity<DurabilityRepairStationComponent> ent, ref DurabilityRepairDoAfterEvent args)
    {
        var component = ent.Comp;
        if (component.ActiveDoAfter == null || component.ActiveItem is not { } item ||
            component.ActiveTool is not { } tool || component.ActiveMaterial is not { } material ||
            args.Used != tool)
        {
            return;
        }

        var user = args.User;
        var completed = !args.Cancelled &&
                        IsStillValid(ent, user, item, tool, material) &&
                        CompleteRepair(ent, user, item, tool, material);

        StopActive(ent);

        if (!completed)
        {
            if (!args.Cancelled)
                Popup(ent, user, "durability-repair-cancelled");

            return;
        }

        // A repair cycle consumes one material unit. Continue only while the item still needs
        // repairs and the user keeps compatible material in hand; TryStartRepair revalidates
        // all other conditions before creating the next DoAfter.
        if (!CanRepairItem(item, component.Tier) ||
            !TryComp(item, out ItemDurabilityComponent? durability) ||
            durability.RepairMaterial is not { } repairMaterial ||
            !TryFindMaterial(user, tool, repairMaterial.Id, out _))
        {
            return;
        }

        TryStartRepair(ent, user, tool, item);
    }

    private void TryStartRepair(Entity<DurabilityRepairStationComponent> ent,
        EntityUid user,
        EntityUid tool,
        EntityUid? preferredItem = null)
    {
        var component = ent.Comp;
        if (component.ActiveDoAfter != null)
        {
            Popup(ent, user, "durability-repair-station-busy");
            return;
        }

        if (!CanOperate(ent))
        {
            Popup(ent, user, component.RequirePower
                ? "durability-repair-station-no-power"
                : "durability-repair-station-not-anchored");
            return;
        }

        if (!_hands.IsHolding(user, tool) || !TryComp(tool, out ToolComponent? toolComponent))
            return;

        var quality = GetRequiredToolQuality(component.Tier);
        if (!_tools.HasQuality(tool, quality, toolComponent))
        {
            Popup(ent, user, "durability-repair-wrong-tool");
            return;
        }

        if (!TryComp(tool, out ItemDurabilityComponent? toolDurability) ||
            !_durability.IsUsable(tool, toolDurability))
        {
            Popup(ent, user, "durability-item-broken");
            return;
        }

        if (!TryFindRepairItem(ent, component.Tier, preferredItem, out var item, out var tierBlocked))
        {
            Popup(ent, user, tierBlocked
                ? "durability-repair-item-tier"
                : "durability-repair-no-item");
            return;
        }

        if (!TryComp(item, out ItemDurabilityComponent? durability) || durability.RepairMaterial is not { } required)
        {
            Popup(ent, user, "durability-repair-no-material-configured");
            return;
        }

        if (!TryFindMaterial(user, tool, required.Id, out var material))
        {
            Popup(ent, user, "durability-repair-no-material");
            return;
        }

        var speed = toolComponent.SpeedModifier > 0f && float.IsFinite(toolComponent.SpeedModifier)
            ? toolComponent.SpeedModifier
            : 1f;
        var delay = TimeSpan.FromSeconds(component.RepairDurationSeconds / speed);
        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            user,
            delay,
            new DurabilityRepairDoAfterEvent(),
            ent.Owner,
            target: ent.Owner,
            used: tool)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnHandChange = true,
            BreakOnDropItem = true,
            NeedHand = true,
            DistanceThreshold = 1.5f,
            DuplicateCondition = DuplicateConditions.SameTarget | DuplicateConditions.SameEvent,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs, out var doAfterId))
            return;

        component.ActiveDoAfter = doAfterId;
        component.ActiveUser = user;
        component.ActiveItem = item;
        component.ActiveTool = tool;
        component.ActiveMaterial = material;
        Dirty(ent);
    }

    private bool CompleteRepair(Entity<DurabilityRepairStationComponent> ent,
        EntityUid user,
        EntityUid item,
        EntityUid tool,
        EntityUid material)
    {
        if (!TryComp(material, out StackComponent? stack) || stack.Count < 1 ||
            !TryComp(item, out ItemDurabilityComponent? durability) ||
            durability.RepairMaterial is not { } required || required.Id != stack.StackTypeId)
        {
            return false;
        }

        var amount = durability.RepairPerMaterial > 0f
            ? durability.RepairPerMaterial
            : ent.Comp.RepairPerMaterial;
        amount = DurabilityMath.Round(amount);
        if (!float.IsFinite(amount) || amount <= 0f ||
            !_stacks.Use(material, 1, stack) ||
            !_durability.TryRepair(item, amount, user, durability))
        {
            return false;
        }

        var toolDrain = tool is { } && TryComp(tool, out ItemDurabilityComponent? toolDurability)
            ? MathF.Max(1f, toolDurability.ToolUseDrain)
            : 1f;
        _durability.TryConsume(tool, toolDrain, DurabilityReason.ToolUse, user);

        if (TryComp(tool, out ToolComponent? toolComponent))
            _tools.PlayToolSound(tool, toolComponent, user);

        Popup(ent, user, "durability-repair-complete");
        return true;
    }

    private bool IsStillValid(Entity<DurabilityRepairStationComponent> ent,
        EntityUid user,
        EntityUid item,
        EntityUid tool,
        EntityUid material)
    {
        if (!Exists(item) || !Exists(tool) || !Exists(material) ||
            !_hands.IsHolding(user, tool) || !_hands.IsHolding(user, material) ||
            _itemSlots.GetItemOrNull(ent, DurabilityRepairStationComponent.RepairSlotId) != item)
        {
            return false;
        }

        return CanOperate(ent);
    }

    private bool TryFindRepairItem(Entity<DurabilityRepairStationComponent> ent,
        int stationTier,
        EntityUid? preferredItem,
        out EntityUid item,
        out bool tierBlocked)
    {
        item = default;
        tierBlocked = false;

        var contained = _itemSlots.GetItemOrNull(ent, DurabilityRepairStationComponent.RepairSlotId);
        if (contained is not { } candidate)
            return false;

        if (preferredItem is { } preferred && preferred == candidate && CanRepairItem(preferred, stationTier))
        {
            item = preferred;
            return true;
        }

        if (!TryComp(candidate, out ItemDurabilityComponent? durability))
            return false;

        if (durability.RequiredWorkbenchTier > stationTier)
        {
            tierBlocked = true;
            return false;
        }

        if (CanRepairItem(candidate, stationTier))
        {
            item = candidate;
            return true;
        }

        return false;
    }

    private bool CanRepairItem(EntityUid item, int stationTier)
    {
        if (!TryComp(item, out ItemDurabilityComponent? durability) ||
            durability.RequiredWorkbenchTier > stationTier || durability.RepairMaterial == null ||
            !float.IsFinite(durability.MaxDurability) || durability.MaxDurability <= 0f)
        {
            return false;
        }

        var current = DurabilityMath.Round(durability.CurrentDurability);
        if (!float.IsFinite(current) || current < 0f)
            current = durability.MaxDurability;

        return DurabilityMath.Round(current) < DurabilityMath.Round(durability.MaxDurability);
    }

    private bool TryFindMaterial(EntityUid user, EntityUid tool, string stackType, out EntityUid material)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held == tool || !TryComp(held, out StackComponent? stack) || stack.Count < 1 ||
                stack.StackTypeId != stackType)
            {
                continue;
            }

            material = held;
            return true;
        }

        material = default;
        return false;
    }

    private bool CanOperate(Entity<DurabilityRepairStationComponent> ent)
    {
        if (!TryComp(ent, out TransformComponent? transform) || !transform.Anchored)
            return false;

        if (!ent.Comp.RequirePower)
            return true;

        return TryComp(ent, out ApcPowerReceiverComponent? receiver) && _power.IsPowered(ent, receiver);
    }

    private void CancelActive(Entity<DurabilityRepairStationComponent> ent)
    {
        if (ent.Comp.ActiveDoAfter is { } doAfter)
            _doAfter.Cancel(doAfter);

        StopActive(ent);
    }

    private void StopActive(Entity<DurabilityRepairStationComponent> ent)
    {
        ent.Comp.ActiveDoAfter = null;
        ent.Comp.ActiveUser = null;
        ent.Comp.ActiveItem = null;
        ent.Comp.ActiveTool = null;
        ent.Comp.ActiveMaterial = null;
        Dirty(ent);
    }

    private void Popup(EntityUid station, EntityUid user, string message)
    {
        _popup.PopupEntity(Loc.GetString(message), station, user);
    }

    private void ConfigureRepairSlot(Entity<DurabilityRepairStationComponent> ent)
    {
        if (!TryComp(ent, out ItemSlotsComponent? itemSlots))
            return;

        if (!_itemSlots.TryGetSlot(ent, DurabilityRepairStationComponent.RepairSlotId, out var slot, itemSlots) ||
            slot.ContainerSlot is not { } container)
        {
            Log.Error($"Entity {ToPrettyString(ent)} has no repair item slot.");
            return;
        }

        container.ShowContents = true;
        container.OccludesLight = false;

        if (TryComp(ent, out ContainerManagerComponent? manager))
            Dirty(ent, manager);
    }

    private static string GetRequiredToolQuality(int tier)
    {
        return tier switch
        {
            1 => "Hammering",
            2 => "Welding",
            _ => "Applicating",
        };
    }

    private string GetStackName(ProtoId<StackPrototype> material)
    {
        return _prototypeManager.TryIndex(material, out StackPrototype? stack) &&
               !string.IsNullOrWhiteSpace(stack.Name)
            ? Loc.GetString(stack.Name)
            : material.Id;
    }

    private string GetToolName(ProtoId<ToolQualityPrototype> quality)
    {
        return _prototypeManager.TryIndex(quality, out ToolQualityPrototype? tool) &&
               !string.IsNullOrWhiteSpace(tool.ToolName)
            ? Loc.GetString(tool.ToolName)
            : quality.Id;
    }
}
