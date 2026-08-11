using System.Linq;
using System.Numerics;
using Content.Shared.Tag;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.ClassProgression;

public enum Wh40kClassRuntimeModifierLayer : byte
{
    Talents,
    Equipment,
    TemporaryEffects,
}

public readonly record struct Wh40kClassRuntimeModifierKey(
    string SourceEffectId,
    Wh40kCharacteristic Characteristic,
    Wh40kClassModifierCategory Category);

public sealed record Wh40kClassRuntimeModifier(
    Wh40kClassRuntimeModifierKey Key,
    Wh40kClassRuntimeModifierLayer Layer,
    int Magnitude,
    TimeSpan? ExpiresAt = null);

public sealed record Wh40kClassEquipmentSnapshot(
    EntityUid Entity,
    IReadOnlySet<ProtoId<TagPrototype>> Tags,
    ProtoId<ItemRarityPrototype> Rarity,
    byte RarityTier,
    float BonusPercent);

public sealed record Wh40kClassRarityModifierData(
    string EffectId,
    ProtoId<TagPrototype> EquipmentTag,
    IReadOnlySet<ProtoId<ItemRarityPrototype>> Rarities,
    byte MinimumTier,
    Wh40kClassRarityParameter Parameter,
    float MaximumBonusPercent);

public sealed record Wh40kResolvedClassEffect(
    string EffectId,
    Wh40kClassSkillEffectKind Kind,
    Wh40kClassEffectSafety Safety,
    Wh40kCharacteristic? Characteristic,
    Wh40kClassModifierCategory ModifierCategory,
    Wh40kClassRuntimeModifierLayer ModifierLayer,
    int Magnitude,
    int MaximumMagnitude,
    EntProtoId? Action,
    TimeSpan Duration,
    TimeSpan MaximumDuration,
    EntityUid? SupportingItem,
    float AppliedRarityPercent)
{
    public Wh40kClassRuntimeMechanic Mechanic { get; init; }
    public Wh40kClassCounterplay Counterplay { get; init; }
    public float Range { get; init; }
    public int MaximumTargets { get; init; }
    public TimeSpan Cooldown { get; init; }
    public bool RequiresEquipment { get; init; }
    public byte? ActionSlot { get; init; }
    public int ActionPriority { get; init; }
    public int SecondaryMagnitude { get; init; }
    public int TertiaryMagnitude { get; init; }
    public float StaminaCost { get; init; }
    public EntProtoId? SecondaryAction { get; init; }
}

/// <summary>
/// Final, safe values passed back to the ranged weapon modifier event after class effects are applied.
/// </summary>
public readonly record struct Wh40kClassGunModifierValues(
    float FireRate,
    float CameraRecoilScalar,
    Angle AngleIncrease,
    Angle AngleDecay,
    Angle MinAngle,
    Angle MaxAngle,
    float ProjectileSpeed);

/// <summary>
/// Pure rules used by the server adapter and tests. Inputs are snapshots; no entity or prototype state is mutated.
/// </summary>
public static class Wh40kClassRuntimePolicy
{
    public const float OverseerCommandRadius = 10f;
    public const float MaximumRarityBonusPercent = 200f;

    /// <summary>
    /// Chooses at most four active ability entries. A secondary action belongs to its primary effect and therefore
    /// cannot consume an additional active-ability slot.
    /// </summary>
    public static IReadOnlyList<Wh40kResolvedClassEffect> SelectGrantedActionEffects(
        IReadOnlyList<Wh40kResolvedClassEffect> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);

        var actionEffects = desired
            .Where(effect => effect.Action != null)
            .OrderByDescending(effect => effect.ActionPriority)
            .ThenBy(effect => effect.EffectId, StringComparer.Ordinal)
            .ToArray();
        var selected = actionEffects
            .Where(effect => effect.ActionSlot != null)
            .GroupBy(effect => effect.ActionSlot!.Value)
            .Select(group => group.First())
            .OrderBy(effect => effect.ActionSlot)
            .Take(Wh40kClassProgressionConstants.MaximumActiveAbilities)
            .ToList();
        var freeSlots = Wh40kClassProgressionConstants.MaximumActiveAbilities - selected.Count;
        selected.AddRange(actionEffects
            .Where(effect => effect.ActionSlot == null)
            .Take(freeSlots));
        return selected;
    }

    /// <summary>
    /// Prevents malformed or unexpectedly compounded modifiers from making a gun's spread limits or multipliers
    /// negative, non-finite, or internally contradictory.
    /// </summary>
    public static Wh40kClassGunModifierValues NormalizeGunModifiers(Wh40kClassGunModifierValues values)
    {
        static float NonNegativeFinite(float value)
        {
            return float.IsFinite(value) ? MathF.Max(0f, value) : 0f;
        }

        static Angle NonNegativeFiniteAngle(Angle value)
        {
            return double.IsFinite(value.Theta)
                ? new Angle(Math.Max(0d, value.Theta))
                : Angle.Zero;
        }

        var minAngle = NonNegativeFiniteAngle(values.MinAngle);
        var maxAngle = NonNegativeFiniteAngle(values.MaxAngle);
        if (maxAngle.Theta < minAngle.Theta)
            maxAngle = minAngle;
        return new Wh40kClassGunModifierValues(
            NonNegativeFinite(values.FireRate),
            NonNegativeFinite(values.CameraRecoilScalar),
            NonNegativeFiniteAngle(values.AngleIncrease),
            NonNegativeFiniteAngle(values.AngleDecay),
            minAngle,
            maxAngle,
            NonNegativeFinite(values.ProjectileSpeed));
    }

    /// <summary>
    /// Returns the multiplier that compensates a pre-applied movement penalty without reversing it or producing an
    /// invalid speed value. Current weapon categories are bounded below this defensive ceiling.
    /// </summary>
    public static float GetPenaltyCompensationMultiplier(int penaltyPercent, float compensation)
    {
        var penalty = Math.Clamp(penaltyPercent, 0, 90) / 100f;
        var safeCompensation = float.IsFinite(compensation)
            ? Math.Clamp(compensation, 0f, 1f)
            : 0f;
        var baseline = 1f - penalty;
        var multiplier = (1f - penalty * (1f - safeCompensation)) / baseline;
        return Math.Clamp(multiplier, 1f, 10f);
    }

    public static bool TryGetSupportingItems(
        IReadOnlyList<Wh40kClassEquipmentSnapshot> equipment,
        IReadOnlyCollection<ProtoId<TagPrototype>> requiredTags,
        out IReadOnlyList<Wh40kClassEquipmentSnapshot> supporting)
    {
        if (requiredTags.Count == 0)
        {
            supporting = equipment;
            return true;
        }

        var matches = equipment
            .Where(item => requiredTags.All(item.Tags.Contains))
            .ToArray();
        supporting = matches;
        return matches.Length > 0;
    }

    public static Wh40kResolvedClassEffect ResolveEffect(
        Wh40kClassSkillEffectPrototype effect,
        IReadOnlyList<Wh40kClassEquipmentSnapshot> supportingItems,
        IReadOnlyList<Wh40kClassRarityModifierPrototype> rarityModifiers,
        Wh40kClassRuntimeModifierLayer modifierLayer = Wh40kClassRuntimeModifierLayer.Talents,
        bool requiresEquipment = false)
    {
        var modifierData = rarityModifiers
            .Select(modifier => new Wh40kClassRarityModifierData(
                modifier.Effect.Id,
                modifier.EquipmentTag,
                modifier.Rarities.ToHashSet(),
                modifier.MinimumTier,
                modifier.Parameter,
                modifier.MaximumBonusPercent))
            .ToArray();
        var magnitudeBonus = GetBestRarityBonus(
            effect.ID,
            Wh40kClassRarityParameter.Magnitude,
            supportingItems,
            modifierData,
            out var magnitudeItem);
        var durationBonus = GetBestRarityBonus(
            effect.ID,
            Wh40kClassRarityParameter.Duration,
            supportingItems,
            modifierData,
            out var durationItem);

        var magnitude = ScaleMagnitude(effect.Magnitude, magnitudeBonus, effect.MaximumMagnitude);
        var duration = ScaleDuration(effect.Duration, durationBonus, effect.MaximumDuration);
        var appliedBonus = MathF.Max(magnitudeBonus, durationBonus);

        return new Wh40kResolvedClassEffect(
            effect.ID,
            effect.Kind,
            effect.Safety,
            effect.Characteristic,
            effect.ModifierCategory,
            modifierLayer,
            magnitude,
            effect.MaximumMagnitude,
            effect.Action,
            duration,
            effect.MaximumDuration,
            magnitudeBonus >= durationBonus
                ? magnitudeItem ?? (requiresEquipment ? supportingItems.FirstOrDefault()?.Entity : null)
                : durationItem ?? (requiresEquipment ? supportingItems.FirstOrDefault()?.Entity : null),
            appliedBonus)
        {
            Mechanic = effect.Mechanic,
            Counterplay = effect.Counterplay,
            Range = effect.Range,
            MaximumTargets = effect.MaximumTargets,
            Cooldown = effect.Cooldown,
            RequiresEquipment = requiresEquipment,
            ActionSlot = effect.ActionSlot,
            ActionPriority = effect.ActionPriority,
            SecondaryMagnitude = effect.SecondaryMagnitude,
            TertiaryMagnitude = effect.TertiaryMagnitude,
            StaminaCost = effect.StaminaCost,
            SecondaryAction = effect.SecondaryAction,
        };
    }

    public static IReadOnlyList<Wh40kClassRuntimeModifier> SelectStrongestModifiers(
        IEnumerable<Wh40kClassRuntimeModifier> modifiers,
        TimeSpan now)
    {
        return modifiers
            .Where(modifier => modifier.ExpiresAt is not { } expiry || expiry > now)
            .GroupBy(modifier => (modifier.Key.Characteristic, modifier.Key.Category))
            .Select(group => group
                .OrderByDescending(modifier => Math.Abs((long) modifier.Magnitude))
                .ThenBy(modifier => modifier.Key.SourceEffectId, StringComparer.Ordinal)
                .First())
            .OrderBy(modifier => modifier.Key.Characteristic)
            .ThenBy(modifier => modifier.Key.Category)
            .ToArray();
    }

    /// <summary>
    /// Resolves live player bodies for an Overseer order. A null party set means the broad no-party
    /// mode; a non-null set is authoritative even when it is empty.
    /// </summary>
    public static IReadOnlyList<Wh40kCommandRecipientCandidate> SelectCommandRecipients(
        MapId sourceMap,
        Vector2 sourcePosition,
        IEnumerable<Wh40kCommandRecipientCandidate> candidates,
        IReadOnlySet<NetUserId>? partyMembers)
    {
        var maximumDistanceSquared = OverseerCommandRadius * OverseerCommandRadius;
        return candidates
            .Where(candidate =>
                candidate.Body.IsValid() &&
                candidate.IsLiving &&
                candidate.MapId == sourceMap &&
                Vector2.DistanceSquared(candidate.Position, sourcePosition) <= maximumDistanceSquared &&
                (partyMembers == null || partyMembers.Contains(candidate.UserId)))
            .GroupBy(candidate => candidate.Body)
            .Select(group => group.First())
            .OrderBy(candidate => Vector2.DistanceSquared(candidate.Position, sourcePosition))
            .ThenBy(candidate => candidate.UserId.ToString(), StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<Wh40kClassCommandRecipientState> SelectStrongestCommands(
        IEnumerable<Wh40kClassCommandRecipientState> states,
        TimeSpan now)
    {
        return states
            .Where(state => state.ExpiresAt > now)
            .GroupBy(state => state.Category)
            .Select(group => group
                .OrderByDescending(state => Math.Abs((long) state.Magnitude))
                .ThenBy(state => state.EffectId, StringComparer.Ordinal)
                .ThenBy(state => state.Source.ToString(), StringComparer.Ordinal)
                .First())
            .OrderBy(state => state.Category)
            .ToArray();
    }

    public static IReadOnlyDictionary<Wh40kCharacteristic, int> SumLayer(
        IEnumerable<Wh40kClassRuntimeModifier> selected,
        Wh40kClassRuntimeModifierLayer layer)
    {
        var result = new Dictionary<Wh40kCharacteristic, int>();
        foreach (var modifier in selected.Where(modifier => modifier.Layer == layer))
        {
            result[modifier.Key.Characteristic] = checked(
                result.GetValueOrDefault(modifier.Key.Characteristic) + modifier.Magnitude);
        }

        return result;
    }

    public static bool IsClassActionAllowed(
        Wh40kClassEffectSafety safety,
        KoronusSafetyRule sourceRules,
        KoronusSafetyRule targetRules,
        bool targetIsPlayer,
        bool targetIsProtectedNpc = false,
        bool targetIsSelf = false)
    {
        if (safety == Wh40kClassEffectSafety.SelfOnly)
            return targetIsSelf;
        if (safety == Wh40kClassEffectSafety.NpcOnly)
            return !targetIsPlayer && !targetIsProtectedNpc;
        if (!targetIsPlayer &&
            safety is (Wh40kClassEffectSafety.OffensiveDamage or
                Wh40kClassEffectSafety.OffensiveStamina or
                Wh40kClassEffectSafety.OffensiveControl))
        {
            return true;
        }

        var combined = sourceRules | targetRules;
        var blockedBy = safety switch
        {
            Wh40kClassEffectSafety.OffensiveDamage =>
                KoronusSafetyRule.ClassOffensiveActions | KoronusSafetyRule.PlayerDamage,
            Wh40kClassEffectSafety.OffensiveStamina =>
                KoronusSafetyRule.ClassOffensiveActions | KoronusSafetyRule.PlayerStaminaDamage,
            Wh40kClassEffectSafety.OffensiveControl =>
                KoronusSafetyRule.ClassOffensiveActions | KoronusSafetyRule.PlayerHarmfulInteractions,
            Wh40kClassEffectSafety.DeviceInteraction =>
                KoronusSafetyRule.ClassDeviceInteractions |
                KoronusSafetyRule.MaintenancePanelScrewdriving |
                KoronusSafetyRule.Anchoring |
                KoronusSafetyRule.Deconstruction |
                KoronusSafetyRule.PlayerShipWires |
                KoronusSafetyRule.PlayerShipDeconstruction |
                KoronusSafetyRule.StationProtection,
            Wh40kClassEffectSafety.AreaEffect =>
                KoronusSafetyRule.ClassAreaEffects |
                KoronusSafetyRule.ChemicalEffects |
                KoronusSafetyRule.HandheldEntityPlacement,
            Wh40kClassEffectSafety.Mobility => KoronusSafetyRule.ClassMobilityActions,
            _ => KoronusSafetyRule.None,
        };

        return (combined & blockedBy) == 0;
    }

    public static float GetBestRarityBonus(
        string effectId,
        Wh40kClassRarityParameter parameter,
        IReadOnlyList<Wh40kClassEquipmentSnapshot> supportingItems,
        IReadOnlyList<Wh40kClassRarityModifierData> rarityModifiers,
        out EntityUid? supportingItem)
    {
        var best = 0f;
        supportingItem = null;
        foreach (var modifier in rarityModifiers)
        {
            if (modifier.EffectId != effectId || modifier.Parameter != parameter)
                continue;

            foreach (var item in supportingItems)
            {
                if (!item.Tags.Contains(modifier.EquipmentTag) ||
                    item.RarityTier < modifier.MinimumTier ||
                    modifier.Rarities.Count > 0 && !modifier.Rarities.Contains(item.Rarity))
                {
                    continue;
                }

                var rolled = float.IsFinite(item.BonusPercent)
                    ? MathF.Max(0f, item.BonusPercent)
                    : 0f;
                var cap = float.IsFinite(modifier.MaximumBonusPercent)
                    ? Math.Clamp(modifier.MaximumBonusPercent, 0f, MaximumRarityBonusPercent)
                    : 0f;
                var candidate = MathF.Min(rolled, cap);
                if (candidate <= best)
                    continue;

                best = candidate;
                supportingItem = item.Entity;
            }
        }

        return best;
    }

    internal static int ScaleMagnitude(int original, float bonusPercent, int maximumMagnitude)
    {
        if (original == 0 || bonusPercent <= 0f)
            return original;

        var scaled = (long) Math.Round(
            original * (1d + bonusPercent / 100d),
            MidpointRounding.AwayFromZero);
        if (maximumMagnitude > 0)
            scaled = Math.Clamp(scaled, -maximumMagnitude, maximumMagnitude);
        return checked((int) scaled);
    }

    internal static TimeSpan ScaleDuration(TimeSpan original, float bonusPercent, TimeSpan maximumDuration)
    {
        if (original <= TimeSpan.Zero || bonusPercent <= 0f)
            return original;

        var ticks = (long) Math.Round(
            original.Ticks * (1d + bonusPercent / 100d),
            MidpointRounding.AwayFromZero);
        if (maximumDuration > TimeSpan.Zero)
            ticks = Math.Min(ticks, maximumDuration.Ticks);
        return TimeSpan.FromTicks(ticks);
    }
}

public readonly record struct Wh40kCommandRecipientCandidate(
    NetUserId UserId,
    EntityUid Body,
    MapId MapId,
    Vector2 Position,
    bool IsLiving);
