using System.Globalization;
using System.Linq;
using System.Text;
using Content.Server._WH40K.ItemRarity;
using Content.Server._WH40K.Progression;
using Content.Server.Charges.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Durability;
using Content.Server.Forensics;
using Content.Server.Labels;
using Content.Server.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Server.Medical.SuitSensors;
using Content.Server.PDA.Ringer;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Server.Stack;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.ItemRarity.Components;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Charges.Components;
using Content.Shared.Damage;
using Content.Shared.Durability;
using Content.Shared.Durability.Components;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Labels.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell;
using Content.Shared.PDA;
using Content.Shared.Paper;
using Content.Shared.Roles;
using Content.Shared.Stacks;
using Content.Shared.StatusIcon;
using Content.Shared.Sprite;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.PersistentInventory.Serialization;

public interface IItemStateAdapter
{
    string ComponentId { get; }

    int RestorePriority => 0;

    bool Handles(IComponent component);

    bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error);

    bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error);
}

/// <summary>
/// A sanitizer identifies known runtime components that may be regenerated or safely discarded.
/// Dynamically added components that are neither adapted nor sanitized are reported as omitted.
/// </summary>
public interface IPersistentInventoryComponentSanitizer
{
    bool CanOmit(EntityUid uid, string componentId, IComponent component);
}

public sealed class RewardDeliveryClaimItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;

    public string ComponentId => "Wh40kRewardDeliveryClaim";

    public RewardDeliveryClaimItemStateAdapter(IEntityManager entities)
    {
        _entities = entities;
    }

    public bool Handles(IComponent component)
    {
        return component is Wh40kRewardDeliveryClaimComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var claim = (Wh40kRewardDeliveryClaimComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("user", claim.UserId.ToString("D")),
            ("delivery", claim.DeliveryId.ToString(CultureInfo.InvariantCulture)),
            ("attempt", claim.ClaimAttempt.ToString(CultureInfo.InvariantCulture)),
            ("index", claim.EntityIndex.ToString(CultureInfo.InvariantCulture)),
            ("expected", claim.ExpectedEntities.ToString(CultureInfo.InvariantCulture)));
        if (claim.UserId == Guid.Empty ||
            claim.DeliveryId <= 0 ||
            claim.ClaimAttempt <= 0 ||
            claim.EntityIndex < 0 ||
            claim.ExpectedEntities <= 0 ||
            claim.EntityIndex >= claim.ExpectedEntities)
        {
            error = "Reward delivery claim marker is invalid.";
            return false;
        }

        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 5 ||
            !state.Fields.TryGetValue("user", out var userText) ||
            !Guid.TryParseExact(userText, "D", out var userId) ||
            userId == Guid.Empty ||
            !TryPositiveLong(state, "delivery", out var deliveryId) ||
            !TryPositiveInt(state, "attempt", out var attempt) ||
            !TryNonNegativeInt(state, "index", out var index) ||
            !TryPositiveInt(state, "expected", out var expected) ||
            index >= expected)
        {
            error = "Reward delivery claim marker payload is invalid.";
            return false;
        }

        var claim = _entities.EnsureComponent<Wh40kRewardDeliveryClaimComponent>(uid);
        claim.UserId = userId;
        claim.DeliveryId = deliveryId;
        claim.ClaimAttempt = attempt;
        claim.EntityIndex = index;
        claim.ExpectedEntities = expected;
        _entities.Dirty(uid, claim);
        error = null;
        return true;
    }

    private static bool TryPositiveLong(
        PersistentInventoryComponentState state,
        string field,
        out long value)
    {
        value = default;
        return state.Fields.TryGetValue(field, out var text) &&
               long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
               value > 0;
    }

    private static bool TryPositiveInt(
        PersistentInventoryComponentState state,
        string field,
        out int value)
    {
        value = default;
        return state.Fields.TryGetValue(field, out var text) &&
               int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
               value > 0;
    }

    private static bool TryNonNegativeInt(
        PersistentInventoryComponentState state,
        string field,
        out int value)
    {
        value = default;
        return state.Fields.TryGetValue(field, out var text) &&
               int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
               value >= 0;
    }
}

public sealed class LimitedChargesItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly ChargesSystem _charges;

    public string ComponentId => "LimitedCharges";

    public LimitedChargesItemStateAdapter(IEntityManager entities, ChargesSystem charges)
    {
        _entities = entities;
        _charges = charges;
    }

    public bool Handles(IComponent component)
    {
        return component is LimitedChargesComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var charges = (LimitedChargesComponent) component;
        var maxCharges = charges.MaxCharges;
        var currentCharges = charges.Charges;
        if (maxCharges >= 0 && currentCharges > maxCharges)
        {
            currentCharges = maxCharges;
            if (!_charges.TrySetPersistentCharges(uid, maxCharges, currentCharges, charges))
            {
                state = default!;
                error = "Limited charges state could not be normalized.";
                return false;
            }
        }

        state = StackItemStateAdapter.State(
            ComponentId,
            ("max", maxCharges.ToString(CultureInfo.InvariantCulture)),
            ("current", currentCharges.ToString(CultureInfo.InvariantCulture)));
        if (maxCharges < 0 || currentCharges < 0)
        {
            error = "Limited charges state is outside its valid range.";
            return false;
        }

        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 2 ||
            !state.Fields.TryGetValue("max", out var maxText) ||
            !int.TryParse(maxText, NumberStyles.None, CultureInfo.InvariantCulture, out var max) ||
            !state.Fields.TryGetValue("current", out var currentText) ||
            !int.TryParse(currentText, NumberStyles.None, CultureInfo.InvariantCulture, out var current) ||
            !_entities.TryGetComponent(uid, out LimitedChargesComponent? component) ||
            !_charges.TrySetPersistentCharges(uid, max, current, component))
        {
            error = "Limited charges state is invalid for the restored prototype.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class PersistentInventoryDerivedStateSanitizer : IPersistentInventoryComponentSanitizer
{
    private readonly ComponentTogglerSystem _componentTogglers;

    private static readonly string[] DerivedNamespacePrefixes =
    {
        "Content.Server.Xenoarchaeology.XenoArtifacts.Effects.Components",
        "Content.Server.Xenoarchaeology.XenoArtifacts.Triggers.Components",
    };

    private static readonly HashSet<string> DerivedComponents = new(StringComparer.Ordinal)
    {
        "ActionsContainer",
        "Actions",
        "AutoRecharge",
        "ActiveInputMover",
        "ActiveRadio",
        "ActiveTimerTrigger",
        "ActiveUserInterface",
        "ActiveWeaponMeleeCharge",
        "Appearance",
        "ArtifactExamineTrigger",
        "ArtifactInteractionTrigger",
        "ArtifactTimerTrigger",
        "AttachedClothing",
        "ChamberMagazineAmmoProvider",
        "ContainerContainer",
        "CursorOffsetRequiresWield",
        "EyeCursorOffset",
        "Fixtures",
        "FlashOnTrigger",
        "GroupExamine",
        "HandheldGPS",
        "HandPlaceholderRemoveable",
        "HealthAnalyzer",
        "IdentityBlocker",
        "IgnitionSource",
        "ItemRarityStats",
        "ItemSlots",
        "LanguageSpeaker",
        "MagazineAmmoProvider",
        "MetaData",
        "NameModifier",
        "Physics",
        "PointLight",
        "PersistentInventoryOperation",
        "SpawnArtifact",
        "Sprite",
        "StationRecordKeyStorage",
        "Tag",
        "TelepathicArtifact",
        "Transform",
        "UseDelay",
    };

    public PersistentInventoryDerivedStateSanitizer(ComponentTogglerSystem componentTogglers)
    {
        _componentTogglers = componentTogglers;
    }

    public bool CanOmit(EntityUid uid, string componentId, IComponent component)
    {
        if (DerivedComponents.Contains(componentId))
            return true;

        if (_componentTogglers.ManagesRuntimeComponent(uid, componentId))
            return true;

        var componentNamespace = component.GetType().Namespace;
        if (componentNamespace == null)
            return false;

        foreach (var prefix in DerivedNamespacePrefixes)
        {
            if (componentNamespace.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

public sealed class IdCardItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly IPrototypeManager _prototypes;
    private readonly SharedIdCardSystem _idCards;

    public string ComponentId => "IdCard";

    public IdCardItemStateAdapter(
        IEntityManager entities,
        IPrototypeManager prototypes,
        SharedIdCardSystem idCards)
    {
        _entities = entities;
        _prototypes = prototypes;
        _idCards = idCards;
    }

    public bool Handles(IComponent component)
    {
        return component is IdCardComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var card = (IdCardComponent) component;
        var departments = _idCards.GetJobDepartments(uid, card)
            .Select(department => department.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fullName"] = EncodeNullable(card.FullName),
            ["jobTitle"] = EncodeNullable(card.LocalizedJobTitle),
            ["jobIcon"] = card.JobIcon.Id,
            ["departmentCount"] = departments.Length.ToString(CultureInfo.InvariantCulture),
            ["company"] = card.CompanyName.Id,
            ["bypassLogging"] = card.BypassLogging ? "1" : "0",
        };

        for (var index = 0; index < departments.Length; index++)
            fields[$"department.{index}"] = departments[index];

        state = new PersistentInventoryComponentState(ComponentId, fields);
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!_entities.TryGetComponent(uid, out IdCardComponent? card) ||
            !TryDecodeNullable(state, "fullName", out var fullName) ||
            !TryDecodeNullable(state, "jobTitle", out var jobTitle) ||
            !state.Fields.TryGetValue("jobIcon", out var jobIconId) ||
            !_prototypes.TryIndex<JobIconPrototype>(jobIconId, out var jobIcon) ||
            !state.Fields.TryGetValue("departmentCount", out var departmentCountText) ||
            !int.TryParse(
                departmentCountText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var departmentCount) ||
            departmentCount < 0 ||
            !state.Fields.TryGetValue("company", out var companyId) ||
            !_prototypes.HasIndex<CompanyPrototype>(companyId) ||
            !state.Fields.TryGetValue("bypassLogging", out var bypassText) ||
            bypassText is not ("0" or "1") ||
            state.Fields.Count != 6 + departmentCount)
        {
            error = "ID card state is invalid for the restored prototype.";
            return false;
        }

        var departments = new List<ProtoId<DepartmentPrototype>>(departmentCount);
        for (var index = 0; index < departmentCount; index++)
        {
            if (!state.Fields.TryGetValue($"department.{index}", out var departmentId) ||
                !_prototypes.HasIndex<DepartmentPrototype>(departmentId))
            {
                error = "ID card department is unavailable.";
                return false;
            }

            departments.Add(departmentId);
        }

        if (!_idCards.TryChangeFullName(uid, fullName, card) ||
            !_idCards.TryChangeJobTitle(uid, jobTitle, card) ||
            !_idCards.TryChangeJobIcon(uid, jobIcon, card) ||
            !_idCards.TryChangeCompanyName(uid, companyId, card))
        {
            error = "ID card state cannot be applied.";
            return false;
        }

        if (!_idCards.TryChangePersistentMetadata(uid, departments, bypassText == "1", card))
        {
            error = "ID card metadata cannot be applied.";
            return false;
        }

        error = null;
        return true;
    }

    internal static string EncodeNullable(string? value)
    {
        return value == null
            ? "-"
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    internal static bool TryDecodeNullable(
        PersistentInventoryComponentState state,
        string field,
        out string? value)
    {
        value = null;
        if (!state.Fields.TryGetValue(field, out var encoded))
            return false;
        if (encoded == "-")
            return true;

        try
        {
            value = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class AccessItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly IPrototypeManager _prototypes;
    private readonly SharedAccessSystem _access;

    public string ComponentId => "Access";

    public AccessItemStateAdapter(
        IEntityManager entities,
        IPrototypeManager prototypes,
        SharedAccessSystem access)
    {
        _entities = entities;
        _prototypes = prototypes;
        _access = access;
    }

    public bool Handles(IComponent component)
    {
        return component is AccessComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var access = (AccessComponent) component;
        var tags = access.Tags
            .Select(tag => tag.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["enabled"] = access.Enabled ? "1" : "0",
            ["tagCount"] = tags.Length.ToString(CultureInfo.InvariantCulture),
        };
        for (var index = 0; index < tags.Length; index++)
            fields[$"tag.{index}"] = tags[index];

        state = new PersistentInventoryComponentState(ComponentId, fields);
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!_entities.TryGetComponent(uid, out AccessComponent? access) ||
            !state.Fields.TryGetValue("enabled", out var enabledText) ||
            enabledText is not ("0" or "1") ||
            !state.Fields.TryGetValue("tagCount", out var tagCountText) ||
            !int.TryParse(tagCountText, NumberStyles.None, CultureInfo.InvariantCulture, out var tagCount) ||
            tagCount < 0 ||
            state.Fields.Count != 2 + tagCount)
        {
            error = "Access state is invalid for the restored prototype.";
            return false;
        }

        var tags = new List<ProtoId<AccessLevelPrototype>>(tagCount);
        for (var index = 0; index < tagCount; index++)
        {
            if (!state.Fields.TryGetValue($"tag.{index}", out var tagId) ||
                !_prototypes.HasIndex<AccessLevelPrototype>(tagId))
            {
                error = "Access level is unavailable.";
                return false;
            }

            tags.Add(tagId);
        }

        if (!_access.TrySetTags(uid, tags, access))
        {
            error = "Access state cannot be applied.";
            return false;
        }

        _access.SetAccessEnabled(uid, enabledText == "1", access);
        error = null;
        return true;
    }
}

public sealed class StackItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly StackSystem _stacks;

    public string ComponentId => "Stack";

    public StackItemStateAdapter(IEntityManager entities, StackSystem stacks)
    {
        _entities = entities;
        _stacks = stacks;
    }

    public bool Handles(IComponent component)
    {
        return component is StackComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var stack = (StackComponent) component;
        state = State(ComponentId, ("count", stack.Count.ToString(CultureInfo.InvariantCulture)));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!TrySingleInt(state, "count", out var count) || count < 0)
        {
            error = "Stack count is invalid.";
            return false;
        }

        if (!_entities.TryGetComponent(uid, out StackComponent? stack))
        {
            error = "Restored prototype has no Stack component.";
            return false;
        }

        _stacks.SetCount(uid, count, stack);
        error = null;
        return true;
    }

    internal static PersistentInventoryComponentState State(
        string componentId,
        params (string Name, string Value)[] fields)
    {
        return new PersistentInventoryComponentState(
            componentId,
            fields.ToDictionary(field => field.Name, field => field.Value, StringComparer.Ordinal));
    }

    internal static bool TrySingleInt(
        PersistentInventoryComponentState state,
        string name,
        out int value)
    {
        value = default;
        return state.Fields.Count == 1 &&
               state.Fields.TryGetValue(name, out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

public sealed class BatteryItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly BatterySystem _batteries;

    public string ComponentId => "Battery";

    public BatteryItemStateAdapter(IEntityManager entities, BatterySystem batteries)
    {
        _entities = entities;
        _batteries = batteries;
    }

    public bool Handles(IComponent component)
    {
        return component is BatteryComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var battery = (BatteryComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("chargeBits", BitConverter.SingleToInt32Bits(battery.CurrentCharge).ToString(CultureInfo.InvariantCulture)));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!StackItemStateAdapter.TrySingleInt(state, "chargeBits", out var bits))
        {
            error = "Battery charge is invalid.";
            return false;
        }

        var charge = BitConverter.Int32BitsToSingle(bits);
        if (!float.IsFinite(charge) ||
            !_entities.TryGetComponent(uid, out BatteryComponent? battery) ||
            charge < 0 ||
            charge > battery.MaxCharge)
        {
            error = "Battery charge is outside prototype limits.";
            return false;
        }

        _batteries.SetCharge(uid, charge, battery);
        error = null;
        return true;
    }
}

public sealed class ItemDurabilityStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly ItemDurabilitySystem _durability;

    public string ComponentId => "ItemDurability";

    public ItemDurabilityStateAdapter(IEntityManager entities, ItemDurabilitySystem durability)
    {
        _entities = entities;
        _durability = durability;
    }

    public bool Handles(IComponent component)
    {
        return component is ItemDurabilityComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var durability = (ItemDurabilityComponent) component;
        var maximum = DurabilityMath.Round(durability.MaxDurability);
        var current = DurabilityMath.Round(durability.CurrentDurability);
        if (!float.IsFinite(maximum) ||
            maximum <= 0f ||
            !float.IsFinite(current) ||
            current < 0f ||
            current > maximum ||
            durability.Broken != (current <= 0f) ||
            durability.Broken && durability.DestroyAtZero)
        {
            state = StackItemStateAdapter.State(ComponentId);
            error = "Item durability state is invalid.";
            return false;
        }

        state = StackItemStateAdapter.State(
            ComponentId,
            ("maximumBits", BitConverter.SingleToInt32Bits(maximum).ToString(CultureInfo.InvariantCulture)),
            ("currentBits", BitConverter.SingleToInt32Bits(current).ToString(CultureInfo.InvariantCulture)),
            ("broken", durability.Broken ? "1" : "0"));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 3 ||
            !state.Fields.TryGetValue("maximumBits", out var maximumText) ||
            !int.TryParse(maximumText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maximumBits) ||
            !state.Fields.TryGetValue("currentBits", out var currentText) ||
            !int.TryParse(currentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentBits) ||
            !state.Fields.TryGetValue("broken", out var brokenText) ||
            brokenText is not ("0" or "1") ||
            !_entities.TryGetComponent(uid, out ItemDurabilityComponent? durability) ||
            !_durability.TrySetPersistentInventoryState(
                uid,
                BitConverter.Int32BitsToSingle(maximumBits),
                BitConverter.Int32BitsToSingle(currentBits),
                brokenText == "1",
                durability))
        {
            error = "Item durability state is invalid for the restored prototype.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class PowerCellDrawItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly PowerCellSystem _powerCells;

    public string ComponentId => "PowerCellDraw";

    public PowerCellDrawItemStateAdapter(IEntityManager entities, PowerCellSystem powerCells)
    {
        _entities = entities;
        _powerCells = powerCells;
    }

    public bool Handles(IComponent component)
    {
        return component is PowerCellDrawComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var draw = (PowerCellDrawComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("enabled", draw.Enabled ? "1" : "0"));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 1 ||
            !state.Fields.TryGetValue("enabled", out var enabledText) ||
            enabledText is not ("0" or "1") ||
            !_entities.TryGetComponent(uid, out PowerCellDrawComponent? draw))
        {
            error = "Power-cell draw state is invalid for the restored prototype.";
            return false;
        }

        _powerCells.SetDrawEnabled((uid, draw), enabledText == "1");
        error = null;
        return true;
    }
}

public sealed class LabelItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly LabelSystem _labels;

    public string ComponentId => "Label";

    public LabelItemStateAdapter(IEntityManager entities, LabelSystem labels)
    {
        _entities = entities;
        _labels = labels;
    }

    public bool Handles(IComponent component)
    {
        return component is LabelComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var label = (LabelComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("text", label.CurrentLabel == null
                ? "-"
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(label.CurrentLabel))));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 1 ||
            !state.Fields.TryGetValue("text", out var encoded) ||
            !_entities.TryGetComponent(uid, out LabelComponent? label) ||
            !TryDecode(encoded, out var text))
        {
            error = "Label state is invalid for the restored prototype.";
            return false;
        }

        _labels.Label(uid, text, label: label);
        error = null;
        return true;
    }

    private static bool TryDecode(string encoded, out string? text)
    {
        text = null;
        if (encoded == "-")
            return true;

        try
        {
            text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class PressurizedSolutionItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly IGameTiming _timing;
    private readonly PressurizedSolutionSystem _pressurized;

    public string ComponentId => "PressurizedSolution";
    public int RestorePriority => 100;

    public PressurizedSolutionItemStateAdapter(
        IEntityManager entities,
        IGameTiming timing,
        PressurizedSolutionSystem pressurized)
    {
        _entities = entities;
        _timing = timing;
        _pressurized = pressurized;
    }

    public bool Handles(IComponent component)
    {
        return component is PressurizedSolutionComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var pressurized = (PressurizedSolutionComponent) component;
        var remaining = pressurized.FizzySettleTime <= _timing.CurTime
            ? TimeSpan.Zero
            : pressurized.FizzySettleTime - _timing.CurTime;
        if (remaining > pressurized.FizzinessMaxDuration ||
            !float.IsFinite(pressurized.SprayFizzinessThresholdRoll) ||
            pressurized.SprayFizzinessThresholdRoll is < 0f or > 1f)
        {
            state = StackItemStateAdapter.State(ComponentId);
            error = "Pressurized solution state is invalid.";
            return false;
        }

        state = StackItemStateAdapter.State(
            ComponentId,
            ("remainingTicks", remaining.Ticks.ToString(CultureInfo.InvariantCulture)),
            ("thresholdBits",
                BitConverter.SingleToInt32Bits(pressurized.SprayFizzinessThresholdRoll)
                    .ToString(CultureInfo.InvariantCulture)));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 2 ||
            !state.Fields.TryGetValue("remainingTicks", out var remainingText) ||
            !long.TryParse(remainingText, NumberStyles.None, CultureInfo.InvariantCulture, out var remainingTicks) ||
            remainingTicks < 0 ||
            !state.Fields.TryGetValue("thresholdBits", out var thresholdText) ||
            !int.TryParse(thresholdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var thresholdBits) ||
            !_entities.TryGetComponent(uid, out PressurizedSolutionComponent? pressurized) ||
            !_pressurized.TrySetPersistentInventoryState(
                (uid, pressurized),
                TimeSpan.FromTicks(remainingTicks),
                BitConverter.Int32BitsToSingle(thresholdBits)))
        {
            error = "Pressurized solution state is invalid for the restored prototype.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class DeviceNetworkItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly DeviceNetworkSystem _devices;

    public string ComponentId => "DeviceNetwork";

    public DeviceNetworkItemStateAdapter(IEntityManager entities, DeviceNetworkSystem devices)
    {
        _entities = entities;
        _devices = devices;
    }

    public bool Handles(IComponent component)
    {
        return component is DeviceNetworkComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var device = (DeviceNetworkComponent) component;
        if (device.CustomAddress &&
            (string.IsNullOrWhiteSpace(device.Address) || device.Address.Length > 128))
        {
            state = StackItemStateAdapter.State(ComponentId);
            error = "Device-network address is invalid.";
            return false;
        }

        state = StackItemStateAdapter.State(
            ComponentId,
            ("receiveFrequency", device.ReceiveFrequency?.ToString(CultureInfo.InvariantCulture) ?? "null"),
            ("transmitFrequency", device.TransmitFrequency?.ToString(CultureInfo.InvariantCulture) ?? "null"),
            ("receiveAll", device.ReceiveAll ? "1" : "0"),
            ("autoConnect", device.AutoConnect ? "1" : "0"),
            ("customAddress", device.CustomAddress
                ? Convert.ToBase64String(Encoding.UTF8.GetBytes(device.Address))
                : "-"));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 5 ||
            !TryNullableUInt(state, "receiveFrequency", out var receiveFrequency) ||
            !TryNullableUInt(state, "transmitFrequency", out var transmitFrequency) ||
            !state.Fields.TryGetValue("receiveAll", out var receiveAllText) ||
            receiveAllText is not ("0" or "1") ||
            !state.Fields.TryGetValue("autoConnect", out var autoConnectText) ||
            autoConnectText is not ("0" or "1") ||
            !state.Fields.TryGetValue("customAddress", out var addressText) ||
            !TryDecodeAddress(addressText, out var customAddress) ||
            !_entities.TryGetComponent(uid, out DeviceNetworkComponent? device) ||
            !_devices.TrySetPersistentInventoryState(
                uid,
                receiveFrequency,
                transmitFrequency,
                receiveAllText == "1",
                autoConnectText == "1",
                customAddress,
                device))
        {
            error = "Device-network state is invalid for the restored prototype.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryNullableUInt(
        PersistentInventoryComponentState state,
        string name,
        out uint? value)
    {
        value = null;
        if (!state.Fields.TryGetValue(name, out var text))
            return false;
        if (text == "null")
            return true;
        if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = parsed;
        return true;
    }

    private static bool TryDecodeAddress(string encoded, out string? address)
    {
        address = null;
        if (encoded == "-")
            return true;

        try
        {
            address = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return !string.IsNullOrWhiteSpace(address) && address.Length <= 128;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class RingerItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly RingerSystem _ringers;

    public string ComponentId => "Ringer";

    public RingerItemStateAdapter(IEntityManager entities, RingerSystem ringers)
    {
        _entities = entities;
        _ringers = ringers;
    }

    public bool Handles(IComponent component)
    {
        return component is RingerComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var ringer = (RingerComponent) component;
        if (ringer.Ringtone.Length != SharedRingerSystem.RingtoneLength ||
            ringer.Ringtone.Any(note => !Enum.IsDefined(note)))
        {
            state = StackItemStateAdapter.State(ComponentId);
            error = "Ringer state is invalid.";
            return false;
        }

        state = new PersistentInventoryComponentState(
            ComponentId,
            ringer.Ringtone
                .Select((note, index) =>
                    new KeyValuePair<string, string>(
                        $"note.{index}",
                        ((byte) note).ToString(CultureInfo.InvariantCulture)))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        var ringtone = new Note[SharedRingerSystem.RingtoneLength];
        if (state.Fields.Count != ringtone.Length ||
            !_entities.TryGetComponent(uid, out RingerComponent? ringer))
        {
            error = "Ringer state is invalid for the restored prototype.";
            return false;
        }

        for (var index = 0; index < ringtone.Length; index++)
        {
            if (!state.Fields.TryGetValue($"note.{index}", out var noteText) ||
                !byte.TryParse(noteText, NumberStyles.None, CultureInfo.InvariantCulture, out var rawNote) ||
                !Enum.IsDefined((Note) rawNote))
            {
                error = "Ringer note is invalid.";
                return false;
            }

            ringtone[index] = (Note) rawNote;
        }

        if (!_ringers.TrySetPersistentInventoryRingtone(uid, ringtone, ringer))
        {
            error = "Ringer state cannot be applied.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class UnpoweredFlashlightItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly UnpoweredFlashlightSystem _flashlights;

    public string ComponentId => "UnpoweredFlashlight";

    public UnpoweredFlashlightItemStateAdapter(
        IEntityManager entities,
        UnpoweredFlashlightSystem flashlights)
    {
        _entities = entities;
        _flashlights = flashlights;
    }

    public bool Handles(IComponent component)
    {
        return component is UnpoweredFlashlightComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var flashlight = (UnpoweredFlashlightComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("lightOn", flashlight.LightOn ? "1" : "0"));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 1 ||
            !state.Fields.TryGetValue("lightOn", out var lightOnText) ||
            lightOnText is not ("0" or "1") ||
            !_entities.TryGetComponent(uid, out UnpoweredFlashlightComponent? flashlight))
        {
            error = "Unpowered flashlight state is invalid for the restored prototype.";
            return false;
        }

        _flashlights.SetLight((uid, flashlight), lightOnText == "1", quiet: true);
        error = null;
        return true;
    }
}

public sealed class SuitSensorItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly SuitSensorSystem _sensors;

    public string ComponentId => "SuitSensor";

    public SuitSensorItemStateAdapter(IEntityManager entities, SuitSensorSystem sensors)
    {
        _entities = entities;
        _sensors = sensors;
    }

    public bool Handles(IComponent component)
    {
        return component is SuitSensorComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var sensor = (SuitSensorComponent) component;
        if (!Enum.IsDefined(sensor.Mode))
        {
            state = StackItemStateAdapter.State(ComponentId);
            error = "Suit-sensor mode is invalid.";
            return false;
        }

        state = StackItemStateAdapter.State(
            ComponentId,
            ("mode", ((byte) sensor.Mode).ToString(CultureInfo.InvariantCulture)),
            ("controlsLocked", sensor.ControlsLocked ? "1" : "0"),
            ("jammed", sensor.Jammed ? "1" : "0"),
            ("iffSignatureEnabled", sensor.IFFSignatureEnabled ? "1" : "0"));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 4 ||
            !state.Fields.TryGetValue("mode", out var modeText) ||
            !byte.TryParse(modeText, NumberStyles.None, CultureInfo.InvariantCulture, out var rawMode) ||
            !Enum.IsDefined((SuitSensorMode) rawMode) ||
            !TryBoolean(state, "controlsLocked", out var controlsLocked) ||
            !TryBoolean(state, "jammed", out var jammed) ||
            !TryBoolean(state, "iffSignatureEnabled", out var iffSignatureEnabled) ||
            !_entities.TryGetComponent(uid, out SuitSensorComponent? sensor) ||
            !_sensors.TrySetPersistentInventoryState(
                (uid, sensor),
                (SuitSensorMode) rawMode,
                controlsLocked,
                jammed,
                iffSignatureEnabled))
        {
            error = "Suit-sensor state is invalid for the restored prototype.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryBoolean(
        PersistentInventoryComponentState state,
        string name,
        out bool value)
    {
        value = false;
        if (!state.Fields.TryGetValue(name, out var text) || text is not ("0" or "1"))
            return false;

        value = text == "1";
        return true;
    }
}

public sealed class DamageItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly DamageableSystem _damageable;

    public string ComponentId => "Damageable";

    public DamageItemStateAdapter(IEntityManager entities, DamageableSystem damageable)
    {
        _entities = entities;
        _damageable = damageable;
    }

    public bool Handles(IComponent component)
    {
        return component is DamageableComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var damageable = (DamageableComponent) component;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["count"] = damageable.Damage.DamageDict.Count.ToString(CultureInfo.InvariantCulture),
        };

        var index = 0;
        foreach (var (damageType, value) in damageable.Damage.DamageDict.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            fields[$"{index}.type"] = damageType;
            fields[$"{index}.value"] = value.Value.ToString(CultureInfo.InvariantCulture);
            index++;
        }

        state = new PersistentInventoryComponentState(ComponentId, fields);
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!TryReadCount(state.Fields, 2, out var count) ||
            !_entities.TryGetComponent(uid, out DamageableComponent? damageable))
        {
            error = "Damage state is invalid for the restored prototype.";
            return false;
        }

        var damage = new DamageSpecifier();
        for (var index = 0; index < count; index++)
        {
            if (!state.Fields.TryGetValue($"{index}.type", out var damageType) ||
                string.IsNullOrWhiteSpace(damageType) ||
                !state.Fields.TryGetValue($"{index}.value", out var rawText) ||
                !int.TryParse(rawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw) ||
                !damage.DamageDict.TryAdd(damageType, FixedPoint2.FromHundredths(raw)))
            {
                error = "Damage entry is invalid.";
                return false;
            }
        }

        _damageable.SetDamage(uid, damageable, damage);
        error = null;
        return true;
    }

    internal static bool TryReadCount(
        IReadOnlyDictionary<string, string> fields,
        int fieldsPerEntry,
        out int count)
    {
        count = default;
        return fields.TryGetValue("count", out var countText) &&
               int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out count) &&
               count >= 0 &&
               fields.Count == 1 + count * fieldsPerEntry;
    }
}

public sealed class BasicAmmoItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly GunSystem _guns;

    public string ComponentId => "BasicEntityAmmoProvider";

    public BasicAmmoItemStateAdapter(IEntityManager entities, GunSystem guns)
    {
        _entities = entities;
        _guns = guns;
    }

    public bool Handles(IComponent component)
    {
        return component is BasicEntityAmmoProviderComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var ammo = (BasicEntityAmmoProviderComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("count", ammo.Count?.ToString(CultureInfo.InvariantCulture) ?? "null"));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 1 ||
            !state.Fields.TryGetValue("count", out var text) ||
            !_entities.TryGetComponent(uid, out BasicEntityAmmoProviderComponent? ammo))
        {
            error = "Basic ammo state is invalid for the restored prototype.";
            return false;
        }

        if (text == "null")
        {
            if (ammo.Count != null)
            {
                error = "Basic ammo nullability no longer matches the prototype.";
                return false;
            }

            error = null;
            return true;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            count < 0 ||
            !_guns.UpdateBasicEntityAmmoCount(uid, count, ammo))
        {
            error = "Basic ammo count is outside prototype limits.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class BallisticAmmoItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly GunSystem _guns;

    public string ComponentId => "BallisticAmmoProvider";

    public BallisticAmmoItemStateAdapter(IEntityManager entities, GunSystem guns)
    {
        _entities = entities;
        _guns = guns;
    }

    public bool Handles(IComponent component)
    {
        return component is BallisticAmmoProviderComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var ammo = (BallisticAmmoProviderComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("unspawnedCount",
                (ammo.Count - ammo.Container.ContainedEntities.Count).ToString(CultureInfo.InvariantCulture)));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!StackItemStateAdapter.TrySingleInt(state, "unspawnedCount", out var count) ||
            count < 0 ||
            !_entities.TryGetComponent(uid, out BallisticAmmoProviderComponent? ammo) ||
            count > ammo.Capacity)
        {
            error = "Ballistic ammo count is outside prototype limits.";
            return false;
        }

        _guns.SetBallisticUnspawned((uid, ammo), count);
        error = null;
        return true;
    }
}

public sealed class GunItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly GunSystem _guns;

    public string ComponentId => "Gun";

    public GunItemStateAdapter(IEntityManager entities, GunSystem guns)
    {
        _entities = entities;
        _guns = guns;
    }

    public bool Handles(IComponent component)
    {
        return component is GunComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var gun = (GunComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("selectedMode", ((byte) gun.SelectedMode).ToString(CultureInfo.InvariantCulture)));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!StackItemStateAdapter.TrySingleInt(state, "selectedMode", out var rawMode) ||
            rawMode is < byte.MinValue or > byte.MaxValue ||
            !_entities.TryGetComponent(uid, out GunComponent? gun) ||
            !_guns.TrySetPersistentInventoryFireMode(uid, gun, (SelectiveFire) rawMode))
        {
            error = "Gun fire mode is invalid for the restored prototype.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class OpenableItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly OpenableSystem _openable;

    public string ComponentId => "Openable";

    public OpenableItemStateAdapter(IEntityManager entities, OpenableSystem openable)
    {
        _entities = entities;
        _openable = openable;
    }

    public bool Handles(IComponent component)
    {
        return component is OpenableComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var openable = (OpenableComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("opened", openable.Opened ? "1" : "0"));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 1 ||
            !state.Fields.TryGetValue("opened", out var openedText) ||
            openedText is not ("0" or "1") ||
            !_entities.TryGetComponent(uid, out OpenableComponent? openable))
        {
            error = "Openable state is invalid for the restored prototype.";
            return false;
        }

        _openable.SetOpen(uid, openedText == "1", openable);
        error = null;
        return true;
    }
}

public sealed class PaperItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly PaperSystem _paper;

    public string ComponentId => "Paper";

    public PaperItemStateAdapter(IEntityManager entities, PaperSystem paper)
    {
        _entities = entities;
        _paper = paper;
    }

    public bool Handles(IComponent component)
    {
        return component is PaperComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var paper = (PaperComponent) component;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["content"] = IdCardItemStateAdapter.EncodeNullable(paper.Content),
            ["stampState"] = IdCardItemStateAdapter.EncodeNullable(paper.StampState),
            ["editingDisabled"] = paper.EditingDisabled ? "1" : "0",
            ["stampCount"] = paper.StampedBy.Count.ToString(CultureInfo.InvariantCulture),
        };

        for (var index = 0; index < paper.StampedBy.Count; index++)
        {
            var stamp = paper.StampedBy[index];
            fields[$"stamp.{index}.name"] =
                IdCardItemStateAdapter.EncodeNullable(stamp.StampedName);
            fields[$"stamp.{index}.color"] = stamp.StampedColor.ToHex();
            fields[$"stamp.{index}.type"] = ((int) stamp.Type).ToString(CultureInfo.InvariantCulture);
            fields[$"stamp.{index}.reapply"] = stamp.Reapply ? "1" : "0";
        }

        state = new PersistentInventoryComponentState(ComponentId, fields);
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!IdCardItemStateAdapter.TryDecodeNullable(state, "content", out var content) ||
            content == null ||
            !IdCardItemStateAdapter.TryDecodeNullable(state, "stampState", out var stampState) ||
            !state.Fields.TryGetValue("editingDisabled", out var editingDisabledText) ||
            editingDisabledText is not ("0" or "1") ||
            !state.Fields.TryGetValue("stampCount", out var stampCountText) ||
            !int.TryParse(stampCountText, NumberStyles.None, CultureInfo.InvariantCulture, out var stampCount) ||
            stampCount < 0 ||
            stampCount > state.Fields.Count ||
            state.Fields.Count != 4 + stampCount * 4 ||
            !_entities.TryGetComponent(uid, out PaperComponent? paper))
        {
            error = "Paper state is invalid for the restored prototype.";
            return false;
        }

        var stamps = new List<StampDisplayInfo>(stampCount);
        for (var index = 0; index < stampCount; index++)
        {
            if (!IdCardItemStateAdapter.TryDecodeNullable(
                    state,
                    $"stamp.{index}.name",
                    out var stampedName) ||
                stampedName == null ||
                !state.Fields.TryGetValue($"stamp.{index}.color", out var colorText) ||
                Color.TryFromHex(colorText) is not { } color ||
                !state.Fields.TryGetValue($"stamp.{index}.type", out var typeText) ||
                !int.TryParse(typeText, NumberStyles.None, CultureInfo.InvariantCulture, out var rawType) ||
                !Enum.IsDefined(typeof(StampType), rawType) ||
                !state.Fields.TryGetValue($"stamp.{index}.reapply", out var reapplyText) ||
                reapplyText is not ("0" or "1"))
            {
                error = "Paper stamp state is invalid.";
                return false;
            }

            stamps.Add(new StampDisplayInfo
            {
                StampedName = stampedName,
                StampedColor = color,
                Type = (StampType) rawType,
                Reapply = reapplyText == "1",
            });
        }

        if (!_paper.TrySetPersistentInventoryState(
                uid,
                content,
                stamps,
                stampState,
                editingDisabledText == "1",
                paper))
        {
            error = "Paper state cannot be applied to the restored prototype.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class FiberItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;

    public string ComponentId => "Fiber";

    public FiberItemStateAdapter(IEntityManager entities)
    {
        _entities = entities;
    }

    public bool Handles(IComponent component)
    {
        return component is FiberComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var fiber = (FiberComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("material", fiber.FiberMaterial.Id),
            ("color", IdCardItemStateAdapter.EncodeNullable(fiber.FiberColor)),
            ("fingerprint", IdCardItemStateAdapter.EncodeNullable(fiber.Fiberprint)));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 3 ||
            !state.Fields.TryGetValue("material", out var material) ||
            string.IsNullOrWhiteSpace(material) ||
            !IdCardItemStateAdapter.TryDecodeNullable(state, "color", out var color) ||
            !IdCardItemStateAdapter.TryDecodeNullable(state, "fingerprint", out var fingerprint))
        {
            error = "Fiber state is invalid.";
            return false;
        }

        var fiber = _entities.EnsureComponent<FiberComponent>(uid);
        fiber.FiberMaterial = material;
        fiber.FiberColor = color;
        fiber.Fiberprint = fingerprint;
        _entities.Dirty(uid, fiber);
        error = null;
        return true;
    }
}

public sealed class RandomSpriteItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;

    public string ComponentId => "RandomSprite";

    public RandomSpriteItemStateAdapter(IEntityManager entities)
    {
        _entities = entities;
    }

    public bool Handles(IComponent component)
    {
        return component is RandomSpriteComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var randomSprite = (RandomSpriteComponent) component;
        var selected = randomSprite.Selected
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["count"] = selected.Length.ToString(CultureInfo.InvariantCulture),
        };

        for (var index = 0; index < selected.Length; index++)
        {
            var (layer, selection) = selected[index];
            fields[$"{index}.layer"] = IdCardItemStateAdapter.EncodeNullable(layer);
            fields[$"{index}.state"] = IdCardItemStateAdapter.EncodeNullable(selection.State);
            fields[$"{index}.hasColor"] = selection.Color.HasValue ? "1" : "0";
            fields[$"{index}.color"] = selection.Color?.ToHex() ?? "-";
        }

        state = new PersistentInventoryComponentState(ComponentId, fields);
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!state.Fields.TryGetValue("count", out var countText) ||
            !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count < 0 ||
            count > state.Fields.Count ||
            state.Fields.Count != 1 + count * 4)
        {
            error = "Random sprite state is invalid.";
            return false;
        }

        var selected = new Dictionary<string, (string State, Color? Color)>(count, StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            if (!IdCardItemStateAdapter.TryDecodeNullable(state, $"{index}.layer", out var layer) ||
                string.IsNullOrWhiteSpace(layer) ||
                !IdCardItemStateAdapter.TryDecodeNullable(state, $"{index}.state", out var spriteState) ||
                string.IsNullOrWhiteSpace(spriteState) ||
                !state.Fields.TryGetValue($"{index}.hasColor", out var hasColorText) ||
                hasColorText is not ("0" or "1") ||
                !state.Fields.TryGetValue($"{index}.color", out var colorText))
            {
                error = "Random sprite selection is invalid.";
                return false;
            }

            Color? color = null;
            if (hasColorText == "1")
            {
                color = Color.TryFromHex(colorText);
                if (color == null)
                {
                    error = "Random sprite color is invalid.";
                    return false;
                }
            }
            else if (colorText != "-")
            {
                error = "Random sprite color marker is invalid.";
                return false;
            }

            if (!selected.TryAdd(layer, (spriteState, color)))
            {
                error = "Random sprite state contains duplicate layers.";
                return false;
            }
        }

        var randomSprite = _entities.EnsureComponent<RandomSpriteComponent>(uid);
        randomSprite.Selected = selected;
        _entities.Dirty(uid, randomSprite);
        error = null;
        return true;
    }
}

public sealed class ExpendableLightItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly ExpendableLightSystem _lights;

    public string ComponentId => "ExpendableLight";

    public ExpendableLightItemStateAdapter(
        IEntityManager entities,
        ExpendableLightSystem lights)
    {
        _entities = entities;
        _lights = lights;
    }

    public bool Handles(IComponent component)
    {
        return component is ExpendableLightComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var light = (ExpendableLightComponent) component;
        if (!Enum.IsDefined(light.CurrentState))
        {
            state = StackItemStateAdapter.State(ComponentId);
            error = "Expendable light state is invalid.";
            return false;
        }

        state = StackItemStateAdapter.State(
            ComponentId,
            ("spent", light.CurrentState == ExpendableLightState.BrandNew ? "0" : "1"));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 1 ||
            !state.Fields.TryGetValue("spent", out var spentText) ||
            spentText is not ("0" or "1") ||
            !_entities.TryGetComponent(uid, out ExpendableLightComponent? light) ||
            !_lights.TrySetPersistentInventorySpent(uid, spentText == "1", light))
        {
            error = "Expendable light state is invalid for the restored prototype.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class ItemRarityItemStateAdapter : IItemStateAdapter
{
    private readonly ItemRarityRandomizationSystem _rarity;

    public string ComponentId => "ItemRarity";

    public int RestorePriority => -100;

    public ItemRarityItemStateAdapter(ItemRarityRandomizationSystem rarity)
    {
        _rarity = rarity;
    }

    public bool Handles(IComponent component)
    {
        return component is ItemRarityComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var rarity = (ItemRarityComponent) component;
        state = StackItemStateAdapter.State(
            ComponentId,
            ("rarity", rarity.Rarity.Id),
            ("bonusBits", BitConverter.SingleToInt32Bits(rarity.BonusPercent)
                .ToString(CultureInfo.InvariantCulture)),
            ("isRolled", rarity.IsRolled ? "1" : "0"),
            ("worldEffectSuppressed", rarity.WorldEffectSuppressed ? "1" : "0"));
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (state.Fields.Count != 4 ||
            !state.Fields.TryGetValue("rarity", out var rarityId) ||
            string.IsNullOrWhiteSpace(rarityId) ||
            !state.Fields.TryGetValue("bonusBits", out var bonusText) ||
            !int.TryParse(bonusText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bonusBits) ||
            !state.Fields.TryGetValue("isRolled", out var isRolledText) ||
            isRolledText is not ("0" or "1") ||
            !state.Fields.TryGetValue("worldEffectSuppressed", out var suppressedText) ||
            suppressedText is not ("0" or "1") ||
            !_rarity.TrySetPersistentInventoryState(
                uid,
                new ProtoId<ItemRarityPrototype>(rarityId),
                BitConverter.Int32BitsToSingle(bonusBits),
                isRolledText == "1",
                suppressedText == "1"))
        {
            error = "Item rarity state is invalid for the restored prototype.";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class ForensicsItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;

    public string ComponentId => "Forensics";

    public ForensicsItemStateAdapter(IEntityManager entities)
    {
        _entities = entities;
    }

    public bool Handles(IComponent component)
    {
        return component is ForensicsComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var forensics = (ForensicsComponent) component;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cleanDistanceBits"] = BitConverter.SingleToInt32Bits(forensics.CleanDistance)
                .ToString(CultureInfo.InvariantCulture),
            ["canDnaBeCleaned"] = forensics.CanDnaBeCleaned ? "1" : "0",
        };

        WriteSet(fields, "fingerprint", forensics.Fingerprints);
        WriteSet(fields, "fiber", forensics.Fibers);
        WriteSet(fields, "dna", forensics.DNAs);
        WriteSet(fields, "residue", forensics.Residues);

        state = new PersistentInventoryComponentState(ComponentId, fields);
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        var consumedFields = 2;
        if (!state.Fields.TryGetValue("cleanDistanceBits", out var cleanDistanceText) ||
            !int.TryParse(
                cleanDistanceText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var cleanDistanceBits) ||
            !state.Fields.TryGetValue("canDnaBeCleaned", out var canDnaBeCleanedText) ||
            canDnaBeCleanedText is not ("0" or "1") ||
            !TryReadSet(state, "fingerprint", ref consumedFields, out var fingerprints) ||
            !TryReadSet(state, "fiber", ref consumedFields, out var fibers) ||
            !TryReadSet(state, "dna", ref consumedFields, out var dnas) ||
            !TryReadSet(state, "residue", ref consumedFields, out var residues) ||
            state.Fields.Count != consumedFields)
        {
            error = "Forensics state is invalid.";
            return false;
        }

        var cleanDistance = BitConverter.Int32BitsToSingle(cleanDistanceBits);
        if (!float.IsFinite(cleanDistance) || cleanDistance < 0f)
        {
            error = "Forensics clean distance is invalid.";
            return false;
        }

        var forensics = _entities.EnsureComponent<ForensicsComponent>(uid);
        forensics.Fingerprints = fingerprints;
        forensics.Fibers = fibers;
        forensics.DNAs = dnas;
        forensics.Residues = residues;
        forensics.CleanDistance = cleanDistance;
        forensics.CanDnaBeCleaned = canDnaBeCleanedText == "1";

        error = null;
        return true;
    }

    private static void WriteSet(
        Dictionary<string, string> fields,
        string prefix,
        IReadOnlyCollection<string> values)
    {
        var ordered = values.Order(StringComparer.Ordinal).ToArray();
        fields[$"{prefix}Count"] = ordered.Length.ToString(CultureInfo.InvariantCulture);
        for (var index = 0; index < ordered.Length; index++)
            fields[$"{prefix}.{index}"] = ordered[index];
    }

    private static bool TryReadSet(
        PersistentInventoryComponentState state,
        string prefix,
        ref int consumedFields,
        out HashSet<string> values)
    {
        values = new HashSet<string>(StringComparer.Ordinal);
        if (!state.Fields.TryGetValue($"{prefix}Count", out var countText) ||
            !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count < 0 ||
            count > state.Fields.Count)
        {
            return false;
        }

        consumedFields++;
        for (var index = 0; index < count; index++)
        {
            if (!state.Fields.TryGetValue($"{prefix}.{index}", out var value) ||
                !values.Add(value))
            {
                return false;
            }

            consumedFields++;
        }

        return true;
    }
}

public sealed class SolutionItemStateAdapter : IItemStateAdapter
{
    private readonly IEntityManager _entities;
    private readonly SharedSolutionContainerSystem _solutions;

    public string ComponentId => "SolutionContainerManager";

    public SolutionItemStateAdapter(
        IEntityManager entities,
        SharedSolutionContainerSystem solutions)
    {
        _entities = entities;
        _solutions = solutions;
    }

    public bool Handles(IComponent component)
    {
        return component is SolutionContainerManagerComponent;
    }

    public bool TryCapture(
        EntityUid uid,
        IComponent component,
        out PersistentInventoryComponentState state,
        out string? error)
    {
        var manager = (SolutionContainerManagerComponent) component;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var solutions = _solutions.EnumerateSolutions((uid, manager))
            .Where(entry => entry.Name != null)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        if (solutions.Length != manager.Containers.Count)
        {
            state = StackItemStateAdapter.State(ComponentId);
            error = "Not every declared solution container can be enumerated.";
            return false;
        }

        fields["count"] = solutions.Length.ToString(CultureInfo.InvariantCulture);
        for (var solutionIndex = 0; solutionIndex < solutions.Length; solutionIndex++)
        {
            var (name, solutionEntity) = solutions[solutionIndex];
            var solution = solutionEntity.Comp.Solution;
            fields[$"{solutionIndex}.name"] = name!;
            fields[$"{solutionIndex}.temperatureBits"] =
                BitConverter.SingleToInt32Bits(solution.Temperature).ToString(CultureInfo.InvariantCulture);
            fields[$"{solutionIndex}.reagentCount"] =
                solution.Contents.Count.ToString(CultureInfo.InvariantCulture);

            var reagents = solution.Contents
                .OrderBy(reagent => reagent.Reagent.Prototype, StringComparer.Ordinal)
                .ToArray();
            for (var reagentIndex = 0; reagentIndex < reagents.Length; reagentIndex++)
            {
                var reagent = reagents[reagentIndex];
                if (reagent.Reagent.Data is { Count: > 0 })
                {
                    state = StackItemStateAdapter.State(ComponentId);
                    error = $"Solution {name} contains reagent-specific mutable data.";
                    return false;
                }

                fields[$"{solutionIndex}.reagent.{reagentIndex}.prototype"] = reagent.Reagent.Prototype;
                fields[$"{solutionIndex}.reagent.{reagentIndex}.quantity"] =
                    reagent.Quantity.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        state = new PersistentInventoryComponentState(ComponentId, fields);
        error = null;
        return true;
    }

    public bool TryRestore(
        EntityUid uid,
        PersistentInventoryComponentState state,
        out string? error)
    {
        if (!_entities.TryGetComponent(uid, out SolutionContainerManagerComponent? manager) ||
            !state.Fields.TryGetValue("count", out var countText) ||
            !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count < 0)
        {
            error = "Solution state is invalid for the restored prototype.";
            return false;
        }

        var targetSolutions = _solutions.EnumerateSolutions((uid, manager))
            .Where(entry => entry.Name != null)
            .ToDictionary(entry => entry.Name!, entry => entry.Solution, StringComparer.Ordinal);
        if (targetSolutions.Count != count)
        {
            error = "Solution containers no longer match the prototype.";
            return false;
        }

        var consumedFields = 1;
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var solutionIndex = 0; solutionIndex < count; solutionIndex++)
        {
            if (!state.Fields.TryGetValue($"{solutionIndex}.name", out var name) ||
                !seenNames.Add(name) ||
                !targetSolutions.TryGetValue(name, out var target) ||
                !state.Fields.TryGetValue($"{solutionIndex}.temperatureBits", out var temperatureText) ||
                !int.TryParse(temperatureText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var temperatureBits) ||
                !state.Fields.TryGetValue($"{solutionIndex}.reagentCount", out var reagentCountText) ||
                !int.TryParse(reagentCountText, NumberStyles.None, CultureInfo.InvariantCulture, out var reagentCount) ||
                reagentCount < 0)
            {
                error = "Solution entry is invalid.";
                return false;
            }

            consumedFields += 3 + reagentCount * 2;
            var temperature = BitConverter.Int32BitsToSingle(temperatureBits);
            if (!float.IsFinite(temperature))
            {
                error = "Solution temperature is invalid.";
                return false;
            }

            _solutions.RemoveAllSolution(target);
            for (var reagentIndex = 0; reagentIndex < reagentCount; reagentIndex++)
            {
                if (!state.Fields.TryGetValue(
                        $"{solutionIndex}.reagent.{reagentIndex}.prototype",
                        out var prototype) ||
                    string.IsNullOrWhiteSpace(prototype) ||
                    !state.Fields.TryGetValue(
                        $"{solutionIndex}.reagent.{reagentIndex}.quantity",
                        out var quantityText) ||
                    !int.TryParse(quantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawQuantity) ||
                    rawQuantity < 0 ||
                    !_solutions.TryAddReagent(
                        target,
                        prototype,
                        FixedPoint2.FromHundredths(rawQuantity),
                        out var accepted) ||
                    accepted.Value != rawQuantity)
                {
                    error = "Solution reagent is invalid or exceeds prototype capacity.";
                    return false;
                }
            }

            _solutions.SetTemperature(target, temperature);
        }

        if (state.Fields.Count != consumedFields)
        {
            error = "Solution state contains unknown fields.";
            return false;
        }

        error = null;
        return true;
    }
}
