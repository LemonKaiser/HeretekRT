using System.Globalization;
using System.Numerics;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Durability.Components;
using Content.Shared.Durability.Systems;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Silicons.Borgs;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Verbs;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared.Armor;

/// <summary>
///     This handles logic relating to <see cref="ArmorComponent" />
/// </summary>
public abstract partial class SharedArmorSystem : EntitySystem
{
    [Dependency] private ExamineSystemShared _examine = default!;

    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmorComponent, InventoryRelayedEvent<CoefficientQueryEvent>>(OnCoefficientQuery);
        SubscribeLocalEvent<ArmorComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnDamageModify);
        SubscribeLocalEvent<ArmorComponent, BorgModuleRelayedEvent<DamageModifyEvent>>(OnBorgDamageModify);
        SubscribeLocalEvent<ArmorComponent, GetVerbsEvent<ExamineVerb>>(OnArmorVerbExamine);
    }

    /// <summary>
    /// Get the total Damage reduction value of all equipment caught by the relay.
    /// </summary>
    /// <param name="ent">The item that's being relayed to</param>
    /// <param name="args">The event, contains the running count of armor percentage as a coefficient</param>
    private void OnCoefficientQuery(Entity<ArmorComponent> ent, ref InventoryRelayedEvent<CoefficientQueryEvent> args)
    {
        if (TryComp<ItemDurabilityComponent>(ent, out var durability) && durability.Broken)
            return;

        foreach (var armorCoefficient in ent.Comp.Modifiers.Coefficients)
        {
            args.Args.DamageModifiers.Coefficients[armorCoefficient.Key] = args.Args.DamageModifiers.Coefficients.TryGetValue(armorCoefficient.Key, out var coefficient) ? coefficient * armorCoefficient.Value : armorCoefficient.Value;
        }
    }

    private void OnDamageModify(EntityUid uid, ArmorComponent component, InventoryRelayedEvent<DamageModifyEvent> args)
    {
        ApplyArmor(uid, component, args.Args);
    }

    private void OnBorgDamageModify(EntityUid uid, ArmorComponent component,
        ref BorgModuleRelayedEvent<DamageModifyEvent> args)
    {
        ApplyArmor(uid, component, args.Args);
    }

    private void ApplyArmor(EntityUid uid, ArmorComponent component, DamageModifyEvent args)
    {
        if (TryComp<ItemDurabilityComponent>(uid, out var durability) && durability.Broken)
            return;

        var coverage = ResolveCoverage(uid, component, durability);
        if (!ArmorCoverage.Covers(coverage, args.ArmorTargetPart))
            return;

        var hadPositiveDamage = args.Damage.AnyPositive();
        var coveredPartCount = BitOperations.PopCount((uint) (ushort) coverage);
        var broadHit = !args.ArmorTargetPart.HasValue ||
                       args.ArmorTargetPart.Value == TargetBodyPart.All;
        var priority = broadHit ? -coveredPartCount : coveredPartCount;

        var armorRating = component.ArmorRating;
        if (hadPositiveDamage && armorRating > 0f)
            args.ConsiderArmorRatingCandidate(uid, armorRating, priority);

        var beforeModifier = args.Damage;
        args.Damage = DamageSpecifier.ApplyModifierSet(beforeModifier, component.Modifiers);
        var modifierAbsorbedDamage = args.RecordArmorModifier(beforeModifier, args.Damage);

        if (!hadPositiveDamage ||
            !args.TrackArmorDurability ||
            durability is null ||
            durability.ArmorDamageDrainMultiplier <= 0f ||
            !durability.ProtectsWearer ||
            component.ProtectedBodyParts == 0 && durability.ProtectedBodyParts == 0)
        {
            return;
        }

        args.ConsiderArmorDurabilityCandidate(uid, armorRating, priority, modifierAbsorbedDamage);
    }

    private TargetBodyPart ResolveCoverage(EntityUid uid, ArmorComponent component,
        ItemDurabilityComponent? durability)
    {
        if (component.ProtectedBodyParts != 0)
            return component.ProtectedBodyParts;

        // Keep the explicit narrow masks already present on durability prototypes.
        if (durability is not null && durability.ProtectedBodyParts != TargetBodyPart.All &&
            durability.ProtectedBodyParts != 0)
        {
            return durability.ProtectedBodyParts;
        }

        if (TryComp<ClothingComponent>(uid, out var clothing))
        {
            var slots = clothing.InSlotFlag ?? clothing.Slots;
            var inferred = ArmorCoverage.FromSlots(slots);
            if (inferred != 0)
                return inferred;
        }

        return durability?.ProtectedBodyParts ?? TargetBodyPart.All;
    }

    private void OnArmorVerbExamine(EntityUid uid, ArmorComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var examineMarkup = GetArmorExamine(uid, component);

        var ev = new ArmorExamineEvent(examineMarkup);
        RaiseLocalEvent(uid, ref ev);

        _examine.AddDetailedExamineVerb(args, component, examineMarkup,
            Loc.GetString("armor-examinable-verb-text"), "/Textures/Interface/VerbIcons/dot.svg.192dpi.png",
            Loc.GetString("armor-examinable-verb-message"));
    }

    public FormattedMessage GetArmorExamine(EntityUid uid, ArmorComponent armor)
    {
        var msg = new FormattedMessage();
        msg.AddMarkupOrThrow(Loc.GetString("armor-examine"));

        var armorRating = armor.ArmorRating;
        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString("armor-rating-value",
            ("value", FormatExamineValue(armorRating))));

        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString("armor-rating-protection-value",
            ("value", FormatExamineValue(ArmorRatingMath.GetDamageReductionPercent(armorRating)))));

        TryComp<ItemDurabilityComponent>(uid, out var durability);
        var coverage = ResolveCoverage(uid, armor, durability);
        msg.PushNewline();
        msg.AddMarkupOrThrow(Loc.GetString("armor-coverage-summary",
            ("parts", GetArmorCoverageText(coverage))));

        foreach (var coefficientArmor in armor.Modifiers.Coefficients)
        {
            if (coefficientArmor.Value == 1f)
                continue;

            msg.PushNewline();

            var armorType = Loc.GetString("armor-damage-type-" + coefficientArmor.Key.ToLower());
            if (coefficientArmor.Value == 0f)
            {
                msg.AddMarkupOrThrow(Loc.GetString("armor-immunity-value", ("type", armorType)));
                continue;
            }

            if (coefficientArmor.Value < 1f)
            {
                msg.AddMarkupOrThrow(Loc.GetString("armor-resistance-value",
                    ("type", armorType),
                    ("value", FormatExamineValue((1f - coefficientArmor.Value) * 100f))));
                continue;
            }

            msg.AddMarkupOrThrow(Loc.GetString("armor-vulnerability-value",
                ("type", armorType),
                ("value", FormatExamineValue((coefficientArmor.Value - 1f) * 100f))));
        }

        foreach (var flatArmor in armor.Modifiers.FlatReduction)
        {
            msg.PushNewline();

            var armorType = Loc.GetString("armor-damage-type-" + flatArmor.Key.ToLower());
            msg.AddMarkupOrThrow(Loc.GetString("armor-reduction-value",
                ("type", armorType),
                ("value", FormatExamineValue(flatArmor.Value))
            ));
        }

        return msg;
    }

    private static string FormatExamineValue(float value)
    {
        return float.IsFinite(value)
            ? value.ToString("0.#", CultureInfo.InvariantCulture)
            : "0";
    }

    private string GetArmorCoverageText(TargetBodyPart coverage)
    {
        if ((coverage & TargetBodyPart.All) == TargetBodyPart.All)
            return Loc.GetString("armor-coverage-part-all");

        var parts = new List<string>();

        AddCoveragePart(parts, coverage, TargetBodyPart.Head, "head");
        AddCoveragePart(parts, coverage, TargetBodyPart.Torso, "torso");
        AddCoveragePart(parts, coverage, TargetBodyPart.Groin, "groin");
        AddCoveragePair(parts, coverage, TargetBodyPart.Arms,
            TargetBodyPart.LeftArm, TargetBodyPart.RightArm, "arms", "left-arm", "right-arm");
        AddCoveragePair(parts, coverage, TargetBodyPart.Hands,
            TargetBodyPart.LeftHand, TargetBodyPart.RightHand, "hands", "left-hand", "right-hand");
        AddCoveragePair(parts, coverage, TargetBodyPart.Legs,
            TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg, "legs", "left-leg", "right-leg");
        AddCoveragePair(parts, coverage, TargetBodyPart.Feet,
            TargetBodyPart.LeftFoot, TargetBodyPart.RightFoot, "feet", "left-foot", "right-foot");

        return parts.Count == 0
            ? Loc.GetString("armor-coverage-part-unknown")
            : string.Join(", ", parts);
    }

    private void AddCoveragePart(List<string> parts, TargetBodyPart coverage, TargetBodyPart part, string locSuffix)
    {
        if ((coverage & part) != 0)
            parts.Add(Loc.GetString("armor-coverage-part-" + locSuffix));
    }

    private void AddCoveragePair(
        List<string> parts,
        TargetBodyPart coverage,
        TargetBodyPart pair,
        TargetBodyPart left,
        TargetBodyPart right,
        string pairLocSuffix,
        string leftLocSuffix,
        string rightLocSuffix)
    {
        if ((coverage & pair) == pair)
        {
            parts.Add(Loc.GetString("armor-coverage-part-" + pairLocSuffix));
            return;
        }

        AddCoveragePart(parts, coverage, left, leftLocSuffix);
        AddCoveragePart(parts, coverage, right, rightLocSuffix);
    }
}
