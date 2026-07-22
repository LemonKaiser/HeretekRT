using Content.Shared._WH40K.ItemRarity.Components;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Shared._NF.Item;
using Content.Shared.Armor;
using Content.Shared.Durability;
using Content.Shared.Durability.Components;
using Content.Shared.Durability.Events;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Stacks;
using System.Globalization;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.ItemRarity;

/// <summary>
/// Performs the one-time, server-authoritative rarity roll for profiled items.
/// A profile caps the result but never changes the global rarity weights.
/// </summary>
public sealed class ItemRarityRandomizationSystem : EntitySystem
{
    private static readonly ProtoId<ItemRarityPrototype>[] RarityIds =
    [
        ItemRarityPrototypeIds.Stamped,
        ItemRarityPrototypeIds.Consecrated,
        ItemRarityPrototypeIds.MasterCrafted,
        ItemRarityPrototypeIds.Relic,
        ItemRarityPrototypeIds.OmnissiahShrine,
        ItemRarityPrototypeIds.Archeotech,
    ];

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemRarityRandomComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ItemRarityRandomComponent, RandomLootSpawnedEvent>(OnRandomLootSpawned);
        SubscribeLocalEvent<ItemRarityComponent, MapInitEvent>(OnRarityMapInit);
        SubscribeLocalEvent<ItemRarityComponent, DurabilityInitializedEvent>(OnDurabilityInitialized);
        SubscribeLocalEvent<ItemRarityComponent, PickedUpEvent>(OnPickedUp);
        SubscribeLocalEvent<ItemRarityComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ItemRarityComponent, StackSplitEvent>(OnStackSplit);
    }

    private void OnMapInit(Entity<ItemRarityRandomComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.RandomizeOnDirectSpawn)
            Roll(ent.Owner, ent.Comp);
    }

    private void OnRandomLootSpawned(Entity<ItemRarityRandomComponent> ent, ref RandomLootSpawnedEvent args)
    {
        Roll(ent.Owner, ent.Comp);
    }

    private void OnRarityMapInit(Entity<ItemRarityComponent> ent, ref MapInitEvent args)
    {
        TryComp<ItemRarityRandomComponent>(ent.Owner, out var profile);
        ApplyStats(ent.Owner, ent.Comp, profile);
    }

    private void OnDurabilityInitialized(Entity<ItemRarityComponent> ent, ref DurabilityInitializedEvent args)
    {
        TryComp<ItemRarityRandomComponent>(ent.Owner, out var profile);
        ApplyStats(ent.Owner, ent.Comp, profile);
    }

    private void OnPickedUp(Entity<ItemRarityComponent> ent, ref PickedUpEvent args)
    {
        if (ent.Comp.WorldEffectSuppressed)
            return;

        ent.Comp.WorldEffectSuppressed = true;
        Dirty(ent.Owner, ent.Comp);
    }

    private void OnExamined(Entity<ItemRarityComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !ent.Comp.IsRolled)
            return;

        args.PushMarkup(Loc.GetString(
            "item-rarity-bonus-examine",
            ("bonus", FormatValue(ent.Comp.BonusPercent))));
    }

    private void OnStackSplit(Entity<ItemRarityComponent> ent, ref StackSplitEvent args)
    {
        if (TerminatingOrDeleted(args.NewId))
            return;

        var splitRarity = EnsureComp<ItemRarityComponent>(args.NewId);
        splitRarity.Rarity = ent.Comp.Rarity;
        splitRarity.BonusPercent = ent.Comp.BonusPercent;
        splitRarity.IsRolled = ent.Comp.IsRolled;
        splitRarity.WorldEffectSuppressed = ent.Comp.WorldEffectSuppressed;
        Dirty(args.NewId, splitRarity);

        if (!TryComp<ItemRarityStatsComponent>(ent.Owner, out var sourceStats) || !sourceStats.Applied)
            return;

        var splitStats = EnsureComp<ItemRarityStatsComponent>(args.NewId);
        CopyStats(sourceStats, splitStats);
        CopyEffectiveComponents(ent.Owner, args.NewId, sourceStats);
        Dirty(args.NewId, splitStats);
    }

    private void CopyEffectiveComponents(EntityUid source, EntityUid target, ItemRarityStatsComponent stats)
    {
        if (stats.HasArmor && TryComp<ArmorComponent>(source, out var sourceArmor) &&
            TryComp<ArmorComponent>(target, out var targetArmor))
        {
            targetArmor.ArmorRating = sourceArmor.ArmorRating;
            Dirty(target, targetArmor);
        }

        if (stats.HasDurability && TryComp<ItemDurabilityComponent>(source, out var sourceDurability) &&
            TryComp<ItemDurabilityComponent>(target, out var targetDurability))
        {
            targetDurability.MaxDurability = sourceDurability.MaxDurability;
            targetDurability.CurrentDurability = sourceDurability.CurrentDurability;
            targetDurability.Broken = sourceDurability.Broken;
            Dirty(target, targetDurability);
        }

        if (stats.HasMeleeDamage && TryComp<MeleeWeaponComponent>(source, out var sourceMelee) &&
            TryComp<MeleeWeaponComponent>(target, out var targetMelee))
        {
            targetMelee.Damage = new DamageSpecifier(sourceMelee.Damage);
            targetMelee.ArmorPenetration = sourceMelee.ArmorPenetration;
            Dirty(target, targetMelee);
        }

        if (stats.HasWeapon && TryComp<GunComponent>(source, out var sourceGun) &&
            TryComp<GunComponent>(target, out var targetGun))
        {
            targetGun.DamageModifier = sourceGun.DamageModifier;
            Dirty(target, targetGun);
        }
    }

    private static void CopyStats(ItemRarityStatsComponent source, ItemRarityStatsComponent target)
    {
        target.Applied = source.Applied;
        target.HasArmor = source.HasArmor;
        target.BaseArmorRating = source.BaseArmorRating;
        target.HasDurability = source.HasDurability;
        target.BaseMaxDurability = source.BaseMaxDurability;
        target.HasWeapon = source.HasWeapon;
        target.BaseWeaponDamageMultiplier = source.BaseWeaponDamageMultiplier;
        target.EffectiveWeaponDamageMultiplier = source.EffectiveWeaponDamageMultiplier;
        target.BaseWeaponArmorPenetration = source.BaseWeaponArmorPenetration;
        target.EffectiveWeaponArmorPenetration = source.EffectiveWeaponArmorPenetration;
        target.HasMeleeDamage = source.HasMeleeDamage;
        target.BaseMeleeDamage = new DamageSpecifier(source.BaseMeleeDamage);
    }

    private void Roll(EntityUid uid, ItemRarityRandomComponent profile)
    {
        if (TryComp<ItemRarityComponent>(uid, out var existing) && existing.IsRolled)
        {
            ApplyStats(uid, existing, profile);
            return;
        }

        var selected = SelectRarityForRoll(_random.NextFloat(), profile.MaxTier);
        if (selected is null)
            return;

        var minimumBonus = float.IsFinite(selected.BonusMinPercent)
            ? MathF.Max(0f, selected.BonusMinPercent)
            : 0f;
        var maximumBonus = float.IsFinite(selected.BonusMaxPercent)
            ? MathF.Max(0f, selected.BonusMaxPercent)
            : minimumBonus;
        if (maximumBonus < minimumBonus)
            (minimumBonus, maximumBonus) = (maximumBonus, minimumBonus);

        var bonus = minimumBonus >= maximumBonus
            ? minimumBonus
            : _random.NextFloat(minimumBonus, maximumBonus);

        var rarityComponent = EnsureComp<ItemRarityComponent>(uid);
        rarityComponent.Rarity = selected.ID;
        rarityComponent.BonusPercent = bonus;
        rarityComponent.IsRolled = true;
        Dirty(uid, rarityComponent);
        ApplyStats(uid, rarityComponent, profile);
    }

    /// <summary>
    /// Resolves a normalized roll against the configured global weights and
    /// folds the disallowed tail into the highest tier allowed by the profile.
    /// Keeping this deterministic makes the extremely rare thresholds
    /// directly testable without depending on a particular random seed.
    /// </summary>
    public ItemRarityPrototype? SelectRarityForRoll(float normalizedRoll, byte profileMaxTier)
    {
        var maxTier = Math.Clamp(profileMaxTier, (byte) 1, (byte) 6);
        ItemRarityPrototype? highestAllowed = null;
        var totalWeight = 0f;

        foreach (var rarityId in RarityIds)
        {
            if (!_prototypeManager.TryIndex(rarityId, out ItemRarityPrototype? rarity) || rarity is null)
                continue;

            if (!float.IsFinite(rarity.RandomWeight) || rarity.RandomWeight <= 0f)
                continue;

            totalWeight += rarity.RandomWeight;
            if (rarity.Tier <= maxTier)
                highestAllowed = rarity;
        }

        if (totalWeight <= 0f || highestAllowed is null)
            return null;

        var finiteRoll = float.IsFinite(normalizedRoll) ? normalizedRoll : 0f;
        var roll = Math.Clamp(finiteRoll, 0f, MathF.BitDecrement(1f)) * totalWeight;
        ItemRarityPrototype? selected = null;
        var accumulated = 0f;
        foreach (var rarityId in RarityIds)
        {
            if (!_prototypeManager.TryIndex(rarityId, out ItemRarityPrototype? rarity) || rarity is null)
                continue;

            if (!float.IsFinite(rarity.RandomWeight) || rarity.RandomWeight <= 0f)
                continue;

            accumulated += rarity.RandomWeight;
            if (roll <= accumulated)
            {
                selected = rarity;
                break;
            }
        }

        selected ??= highestAllowed;
        if (selected.Tier > maxTier)
            selected = highestAllowed;

        return selected;
    }

    /// <summary>
    /// Applies the rolled bonus once. Durability is deliberately checked before
    /// any mutation so a MapInit ordering difference cannot partially apply the
    /// bonus and then multiply it again later.
    /// </summary>
    private void ApplyStats(EntityUid uid, ItemRarityComponent rarity, ItemRarityRandomComponent? profile = null)
    {
        if (!rarity.IsRolled ||
            TryComp<ItemRarityStatsComponent>(uid, out var currentStats) && currentStats.Applied)
        {
            return;
        }

        if (TryComp<ItemDurabilityComponent>(uid, out var durability) &&
            (!float.IsFinite(durability.MaxDurability) || durability.MaxDurability <= 0f ||
             !float.IsFinite(durability.CurrentDurability) || durability.CurrentDurability < 0f))
        {
            return;
        }

        var multiplier = GetRarityMultiplier(rarity.BonusPercent);
        var stats = EnsureComp<ItemRarityStatsComponent>(uid);

        if (TryComp<ArmorComponent>(uid, out var armor))
        {
            stats.HasArmor = true;
            stats.BaseArmorRating = SanitizeNonNegative(armor.ArmorRating);
            armor.ArmorRating = Scale(stats.BaseArmorRating, multiplier);
            Dirty(uid, armor);
        }

        if (durability is not null)
        {
            stats.HasDurability = true;
            stats.BaseMaxDurability = DurabilityMath.Round(durability.MaxDurability);
            var wearRatio = stats.BaseMaxDurability <= 0f
                ? 0f
                : Math.Clamp(durability.CurrentDurability / stats.BaseMaxDurability, 0f, 1f);
            durability.MaxDurability = DurabilityMath.Round(stats.BaseMaxDurability * multiplier);
            durability.CurrentDurability = DurabilityMath.Clamp(
                DurabilityMath.Round(durability.MaxDurability * wearRatio),
                durability.MaxDurability);
            Dirty(uid, durability);
        }

        TryApplyWeaponStats(uid, stats, multiplier, profile);

        stats.Applied = true;
        Dirty(uid, stats);
    }

    private void TryApplyWeaponStats(
        EntityUid uid,
        ItemRarityStatsComponent stats,
        float rarityMultiplier,
        ItemRarityRandomComponent? profile)
    {
        var hasMelee = TryComp<MeleeWeaponComponent>(uid, out var melee);
        var hasGun = TryComp<GunComponent>(uid, out var gun);
        if (!hasMelee && !hasGun)
            return;

        stats.HasWeapon = true;
        var configuredMultiplier = profile?.BaseWeaponDamageMultiplier ?? 0f;
        var baseMultiplier = configuredMultiplier > 0f
            ? configuredMultiplier
            : hasGun
                ? SanitizePositive(gun!.DamageModifier, 1f)
                : 1f;
        stats.BaseWeaponDamageMultiplier = baseMultiplier;
        stats.EffectiveWeaponDamageMultiplier = Scale(baseMultiplier, rarityMultiplier);

        var configuredArmorPenetration = profile?.BaseWeaponArmorPenetration ?? -1f;
        var baseArmorPenetration = configuredArmorPenetration >= 0f
            ? configuredArmorPenetration
            : hasMelee
                ? SanitizeNonNegative(melee!.ArmorPenetration)
                : 0f;
        stats.BaseWeaponArmorPenetration = baseArmorPenetration;
        stats.EffectiveWeaponArmorPenetration = Scale(baseArmorPenetration, rarityMultiplier);

        if (hasMelee)
        {
            stats.HasMeleeDamage = true;
            stats.BaseMeleeDamage = new DamageSpecifier(melee!.Damage);
            melee.Damage = stats.BaseMeleeDamage * stats.EffectiveWeaponDamageMultiplier;
            melee.ArmorPenetration = stats.EffectiveWeaponArmorPenetration;
            Dirty(uid, melee);
        }

        if (hasGun)
        {
            gun!.DamageModifier = stats.EffectiveWeaponDamageMultiplier;
            Dirty(uid, gun);
        }
    }

    public float GetEffectiveWeaponArmorPenetration(EntityUid uid)
    {
        return TryComp<ItemRarityStatsComponent>(uid, out var stats) && stats.Applied && stats.HasWeapon
            ? stats.EffectiveWeaponArmorPenetration
            : 0f;
    }

    public static float GetRarityMultiplier(float bonusPercent)
    {
        return 1f + MathF.Max(0f, float.IsFinite(bonusPercent) ? bonusPercent : 0f) / 100f;
    }

    private static float Scale(float value, float multiplier)
    {
        var result = SanitizeNonNegative(value) * multiplier;
        return float.IsFinite(result) ? result : 0f;
    }

    private static float SanitizeNonNegative(float value)
    {
        return float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
    }

    private static float SanitizePositive(float value, float fallback)
    {
        return float.IsFinite(value) && value > 0f ? value : fallback;
    }

    private static string FormatValue(float value)
    {
        return float.IsFinite(value)
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : "0";
    }
}
