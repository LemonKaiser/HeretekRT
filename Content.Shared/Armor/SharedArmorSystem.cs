using System.Globalization;
using Content.Shared.Damage;
using Content.Shared.Durability.Components;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Verbs;
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

        var before = args.Damage;
        args.Damage = DamageSpecifier.ApplyModifierSet(before,
            DamageSpecifier.PenetrateArmor(component.Modifiers, args.ArmorPenetration));

        if (args.TrackArmorDurability && durability is { ProtectsWearer: true, ArmorDamageDrainMultiplier: > 0f })
            args.RecordArmorWear(uid, before, args.Damage);
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

}
