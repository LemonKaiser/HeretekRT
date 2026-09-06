using System.Numerics;
using Content.Shared.Tag;
using System.Linq;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared._WH40K.ItemRarity.Prototypes;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.ClassProgression;

public readonly record struct Wh40kClassProgressionDiagnostic(string Code, string Path, string Message);

public sealed record Wh40kClassProgressionValidationResult(
    IReadOnlyList<Wh40kClassProgressionDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;
}

/// <summary>
/// Strict semantic validation for the complete account class catalog.
/// </summary>
public static class Wh40kClassProgressionValidator
{
    public static Wh40kClassProgressionValidationResult Validate(
        IReadOnlyCollection<Wh40kCharacterClassPrototype> classes,
        IReadOnlyCollection<Wh40kClassSpecializationPrototype> specializations,
        IReadOnlyCollection<Wh40kClassSkillPrototype> skills,
        IReadOnlyCollection<Wh40kClassSkillEffectPrototype> effects,
        Func<LocId, bool> hasLocalization,
        Func<Robust.Shared.Utility.ResPath, bool>? hasResource,
        Func<ProtoId<TagPrototype>, bool> hasTag,
        IReadOnlyCollection<Wh40kClassRarityModifierPrototype>? rarityModifiers = null,
        Func<EntProtoId, bool>? hasEntityPrototype = null,
        Func<ProtoId<ItemRarityPrototype>, bool>? hasRarity = null)
    {
        var diagnostics = new List<Wh40kClassProgressionDiagnostic>();
        var classesById = classes.ToDictionary(prototype => prototype.ID, StringComparer.Ordinal);
        var specializationsById = specializations.ToDictionary(prototype => prototype.ID, StringComparer.Ordinal);
        var skillsById = skills.ToDictionary(prototype => prototype.ID, StringComparer.Ordinal);
        var effectsById = effects.ToDictionary(prototype => prototype.ID, StringComparer.Ordinal);
        var usedEffects = new HashSet<string>(StringComparer.Ordinal);
        var effectOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        rarityModifiers ??= Array.Empty<Wh40kClassRarityModifierPrototype>();

        foreach (var characterClass in classes)
        {
            var path = $"wh40kCharacterClass.{characterClass.ID}";
            var declared = characterClass.Specializations.Select(id => id.Id).ToArray();
            if (declared.Length != Wh40kClassProgressionConstants.SpecializationsPerClass)
            {
                Add(diagnostics, "invalid-specialization-count", $"{path}.specializations",
                    $"Class requires exactly {Wh40kClassProgressionConstants.SpecializationsPerClass} specializations.");
            }

            if (declared.Distinct(StringComparer.Ordinal).Count() != declared.Length)
                Add(diagnostics, "duplicate-specialization", $"{path}.specializations", "Specialization ids must be unique.");

            var owned = specializations
                .Where(specialization => specialization.Class.Id == characterClass.ID)
                .OrderBy(specialization => specialization.Order)
                .ToArray();
            if (owned.Length != Wh40kClassProgressionConstants.SpecializationsPerClass)
            {
                Add(diagnostics, "invalid-owned-specialization-count", path,
                    $"Class owns {owned.Length} specialization prototypes instead of two.");
            }

            if (!declared.Order(StringComparer.Ordinal).SequenceEqual(
                    owned.Select(specialization => specialization.ID).Order(StringComparer.Ordinal)))
            {
                Add(diagnostics, "specialization-link-mismatch", $"{path}.specializations",
                    "Class links do not match specialization ownership.");
            }

            if (!owned.Select(specialization => specialization.Order).SequenceEqual(Enumerable.Range(0, owned.Length)))
                Add(diagnostics, "invalid-specialization-order", path, "Specializations must use consecutive order 0 and 1.");

            var positions = new Dictionary<Vector2, string>();
            foreach (var skill in skills.Where(skill =>
                         specializationsById.TryGetValue(skill.Specialization.Id, out var specialization) &&
                         specialization.Class.Id == characterClass.ID))
            {
                if (!positions.TryAdd(skill.DisplayPosition, skill.ID))
                {
                    Add(diagnostics, "duplicate-display-position", $"wh40kClassSkill.{skill.ID}.displayPosition",
                        $"Position is already used by '{positions[skill.DisplayPosition]}'.");
                }
            }
        }

        foreach (var specialization in specializations)
        {
            var path = $"wh40kClassSpecialization.{specialization.ID}";
            if (!classesById.ContainsKey(specialization.Class.Id))
                Add(diagnostics, "missing-class", $"{path}.class", $"Class '{specialization.Class}' does not exist.");
            ValidatePresentation(specialization.Name, specialization.Description, specialization.Icon, path,
                hasLocalization, hasResource, diagnostics);

            var branch = skills
                .Where(skill => skill.Specialization.Id == specialization.ID)
                .OrderBy(skill => skill.Order)
                .ToArray();
            if (specialization.SkillCount != Wh40kClassProgressionConstants.LegacySkillsPerSpecialization &&
                specialization.SkillCount != Wh40kClassProgressionConstants.SkillsPerSpecialization)
            {
                Add(diagnostics, "invalid-specialization-skill-count", $"{path}.skillCount",
                    "Skill count must be either 20 for a legacy tree or 25 for the reworked tree.");
                continue;
            }
            if (branch.Length != specialization.SkillCount)
            {
                Add(diagnostics, "invalid-skill-count", path,
                    $"Specialization requires exactly {specialization.SkillCount} skills.");
                continue;
            }

            var orders = branch.Select(skill => skill.Order).ToArray();
            if (!orders.SequenceEqual(Enumerable.Range(1, specialization.SkillCount)))
                Add(diagnostics, "invalid-skill-order", path,
                    $"Skill orders must form the exact sequence 1 through {specialization.SkillCount}.");

            var branchById = branch.ToDictionary(skill => skill.ID, StringComparer.Ordinal);
            for (var index = 0; index < branch.Length; index++)
            {
                var skill = branch[index];
                if (index == 0 && skill.Prerequisite != null)
                {
                    Add(diagnostics, "invalid-prerequisite", $"wh40kClassSkill.{skill.ID}.prerequisite",
                        "The specialization root must not have a prerequisite.");
                }
                else if (index > 0)
                {
                    if (skill.Prerequisite is not { } prerequisite ||
                        !branchById.TryGetValue(prerequisite.Id, out var parent))
                    {
                        Add(diagnostics, "invalid-prerequisite", $"wh40kClassSkill.{skill.ID}.prerequisite",
                            "Every non-root skill requires a parent from the same specialization.");
                    }
                    else if (parent.Order >= skill.Order)
                    {
                        Add(diagnostics, "invalid-prerequisite-order", $"wh40kClassSkill.{skill.ID}.prerequisite",
                            $"Parent '{parent.ID}' must have a lower order than its child.");
                    }
                }

                var expectedConnections = branch
                    .Where(candidate => candidate.Prerequisite?.Id == skill.ID)
                    .Select(candidate => candidate.ID)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var actualConnections = skill.Connections
                    .Select(connection => connection.Id)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                if (!actualConnections.SequenceEqual(expectedConnections))
                    Add(diagnostics, "invalid-connection-topology", $"wh40kClassSkill.{skill.ID}.connections",
                        "Presentation connections must exactly match the prerequisite children.");

                if (skill.MinimumLevel != Wh40kClassProgressionPolicy.GetMinimumLevelForOrder(skill.Order, specialization.SkillCount))
                    Add(diagnostics, "invalid-minimum-level", $"wh40kClassSkill.{skill.ID}.minimumLevel",
                        "Minimum level does not match the approved rank schedule.");
                if (skill.SharedPurchase == null && skill.Cost != Wh40kClassProgressionConstants.SkillCost)
                    Add(diagnostics, "invalid-skill-cost", $"wh40kClassSkill.{skill.ID}.cost", "Canonical skills cost exactly one point.");
                if (skill.SharedPurchase != null && skill.Cost != 0)
                    Add(diagnostics, "invalid-shared-skill-cost", $"wh40kClassSkill.{skill.ID}.cost", "A mirrored shared skill costs zero points.");
            }
        }

        foreach (var skill in skills)
        {
            var path = $"wh40kClassSkill.{skill.ID}";
            if (!specializationsById.ContainsKey(skill.Specialization.Id))
                Add(diagnostics, "missing-specialization", $"{path}.specialization", "Referenced specialization does not exist.");
            ValidatePresentation(skill.Name, skill.Description, skill.Icon, path,
                hasLocalization, hasResource, diagnostics);

            if (skill.Effects.Count == 0)
            {
                if (skill.SharedPurchase == null)
                    Add(diagnostics, "missing-effects", $"{path}.effects", "Every canonical skill requires at least one typed effect.");
            }
            if (skill.SharedPurchase is { } sharedPurchase)
            {
                if (!skillsById.TryGetValue(sharedPurchase.Id, out var canonical))
                {
                    Add(diagnostics, "missing-shared-skill", $"{path}.sharedPurchase",
                        $"Canonical skill '{sharedPurchase}' does not exist.");
                }
                else if (canonical.SharedPurchase != null || canonical.ID == skill.ID)
                {
                    Add(diagnostics, "invalid-shared-skill", $"{path}.sharedPurchase",
                        "A mirror must reference a distinct canonical skill, not another mirror.");
                }
                else if (canonical.Specialization.Id == skill.Specialization.Id)
                {
                    Add(diagnostics, "invalid-shared-skill-specialization", $"{path}.sharedPurchase",
                        "Shared purchases must bridge the two specializations of a class.");
                }
            }
            if (skill.Effects.Select(effect => effect.Id).Distinct(StringComparer.Ordinal).Count() != skill.Effects.Count)
                Add(diagnostics, "duplicate-effect-reference", $"{path}.effects", "Effect ids must be unique within a skill.");
            foreach (var effectId in skill.Effects)
            {
                if (!effectsById.TryGetValue(effectId.Id, out var effect))
                {
                    Add(diagnostics, "missing-effect", $"{path}.effects", $"Effect '{effectId}' does not exist.");
                    continue;
                }

                usedEffects.Add(effect.ID);
                if (effectOwners.TryGetValue(effect.ID, out var owner) && owner != skill.ID)
                {
                    Add(diagnostics, "shared-effect", $"{path}.effects",
                        $"Effect '{effect.ID}' is already owned by skill '{owner}'.");
                }
                else
                {
                    effectOwners.TryAdd(effect.ID, skill.ID);
                }

                if (skill.Availability == Wh40kClassContentAvailability.Enabled &&
                    effect.Availability != Wh40kClassContentAvailability.Enabled)
                {
                    Add(diagnostics, "unimplemented-effect", $"{path}.availability",
                        $"Enabled skill references coming-soon effect '{effect.ID}'.");
                }

                if (skill.Availability == Wh40kClassContentAvailability.Enabled &&
                    skill.Kind == Wh40kClassSkillKind.Passive &&
                    effect.Action != null)
                {
                    Add(diagnostics, "passive-grants-action", $"{path}.effects",
                        "A passive skill cannot grant an Action.");
                }
            }

            if (skill.Availability == Wh40kClassContentAvailability.Enabled &&
                skill.Kind == Wh40kClassSkillKind.Active &&
                !skill.Effects.Any(effectId => effectsById.TryGetValue(effectId.Id, out var effect) && effect.Action != null))
            {
                Add(diagnostics, "active-without-action", $"{path}.effects",
                    "An enabled active skill requires at least one effect with a concrete Action prototype.");
            }

            foreach (var tag in skill.RequiredEquipmentTags)
            {
                if (!hasTag(tag))
                    Add(diagnostics, "missing-equipment-tag", $"{path}.requiredEquipmentTags", $"Tag '{tag}' does not exist.");
            }

            foreach (var connection in skill.Connections)
            {
                if (!skillsById.TryGetValue(connection.Id, out var connected))
                {
                    Add(diagnostics, "missing-connection", $"{path}.connections", $"Skill '{connection}' does not exist.");
                    continue;
                }

                if (connected.ID == skill.ID)
                    Add(diagnostics, "self-connection", $"{path}.connections", "A skill cannot connect to itself.");
                if (connected.Specialization.Id != skill.Specialization.Id)
                    Add(diagnostics, "foreign-connection", $"{path}.connections", "Connections cannot cross specializations.");
            }

            ValidateNoPrerequisiteCycle(skill, skillsById, diagnostics);
        }

        foreach (var effect in effects)
        {
            if (!usedEffects.Contains(effect.ID))
                Add(diagnostics, "orphan-effect", $"wh40kClassSkillEffect.{effect.ID}", "Effect is not referenced by a skill.");

            ValidateEffect(effect, hasEntityPrototype, diagnostics);
        }

        var rarityModifierIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var modifier in rarityModifiers)
        {
            var path = $"wh40kClassRarityModifier.{modifier.ID}";
            if (!rarityModifierIds.Add(modifier.ID))
                Add(diagnostics, "duplicate-rarity-modifier", path, "Rarity modifier ids must be unique.");
            if (!effectsById.TryGetValue(modifier.Effect.Id, out var effect))
                Add(diagnostics, "missing-rarity-effect", $"{path}.effect", $"Effect '{modifier.Effect}' does not exist.");
            if (!hasTag(modifier.EquipmentTag))
                Add(diagnostics, "missing-rarity-equipment-tag", $"{path}.equipmentTag", $"Tag '{modifier.EquipmentTag}' does not exist.");
            if (modifier.MinimumTier is < 1 or > 6)
                Add(diagnostics, "invalid-rarity-minimum-tier", $"{path}.minimumTier", "Minimum tier must be between 1 and 6.");
            if (!float.IsFinite(modifier.MaximumBonusPercent) ||
                modifier.MaximumBonusPercent <= 0f ||
                modifier.MaximumBonusPercent > Wh40kClassRuntimePolicy.MaximumRarityBonusPercent)
            {
                Add(diagnostics, "invalid-rarity-cap", $"{path}.maximumBonusPercent",
                    $"Rarity cap must be finite and between 0 and {Wh40kClassRuntimePolicy.MaximumRarityBonusPercent} percent.");
            }
            foreach (var rarity in modifier.Rarities)
            {
                if (hasRarity != null && !hasRarity(rarity))
                    Add(diagnostics, "missing-rarity", $"{path}.rarities", $"Rarity '{rarity}' does not exist.");
            }

            if (effect == null)
                continue;
            if (modifier.Parameter == Wh40kClassRarityParameter.Magnitude && effect.MaximumMagnitude <= 0)
                Add(diagnostics, "missing-rarity-magnitude-cap", $"{path}.parameter", "Magnitude scaling requires an effect maximumMagnitude.");
            if (modifier.Parameter == Wh40kClassRarityParameter.Duration && effect.MaximumDuration <= TimeSpan.Zero)
                Add(diagnostics, "missing-rarity-duration-cap", $"{path}.parameter", "Duration scaling requires an effect maximumDuration.");
        }

        foreach (var kind in Enum.GetValues<Wh40kClassSkillEffectKind>())
        {
            if (!effects.Any(effect => effect.Kind == kind))
                Add(diagnostics, "unused-effect-kind", "wh40kClassSkillEffect", $"Catalog does not cover effect kind '{kind}'.");
        }

        return new Wh40kClassProgressionValidationResult(diagnostics);
    }

    private static void ValidateEffect(
        Wh40kClassSkillEffectPrototype effect,
        Func<EntProtoId, bool>? hasEntityPrototype,
        ICollection<Wh40kClassProgressionDiagnostic> diagnostics)
    {
        if (effect.Availability != Wh40kClassContentAvailability.Enabled)
            return;

        var path = $"wh40kClassSkillEffect.{effect.ID}";
        if (effect.Mechanic == Wh40kClassRuntimeMechanic.None)
            Add(diagnostics, "missing-runtime-mechanic", $"{path}.mechanic", "Enabled effects require a closed stage-4 runtime mechanic.");
        if (!float.IsFinite(effect.Range) || effect.Range is < 0f or > 30f)
            Add(diagnostics, "invalid-effect-range", $"{path}.range", "Effect range must be finite and between 0 and 30 tiles.");
        if (effect.MaximumTargets is < 0 or > 32)
            Add(diagnostics, "invalid-effect-target-cap", $"{path}.maximumTargets", "Effect target cap must be between 0 and 32.");
        if (effect.Cooldown < TimeSpan.Zero || effect.Cooldown > TimeSpan.FromMinutes(10))
            Add(diagnostics, "invalid-effect-cooldown", $"{path}.cooldown", "Effect cooldown must be between zero and ten minutes.");
        if (effect.Action is { } declaredAction && hasEntityPrototype != null && !hasEntityPrototype(declaredAction))
            Add(diagnostics, "unknown-action", $"{path}.action", $"Action prototype '{declaredAction}' does not exist.");
        if (effect.SecondaryAction is { } secondaryAction && hasEntityPrototype != null && !hasEntityPrototype(secondaryAction))
            Add(diagnostics, "unknown-secondary-action", $"{path}.secondaryAction", $"Action prototype '{secondaryAction}' does not exist.");
        if (effect.ActionSlot >= Wh40kClassProgressionConstants.MaximumActiveAbilities)
            Add(diagnostics, "invalid-action-slot", $"{path}.actionSlot",
                $"Action slot must be between 0 and {Wh40kClassProgressionConstants.MaximumActiveAbilities - 1}.");
        if (effect.Action == null && effect.ActionSlot != null)
            Add(diagnostics, "action-slot-without-action", $"{path}.actionSlot",
                "Only an effect that grants an action may declare an action slot.");
        if (effect.SecondaryAction != null && effect.Action == null)
            Add(diagnostics, "secondary-action-without-action", $"{path}.secondaryAction",
                "A companion action requires a primary action on the same effect.");
        if (!float.IsFinite(effect.StaminaCost) || effect.StaminaCost < 0f || effect.StaminaCost > 100f)
            Add(diagnostics, "invalid-stamina-cost", $"{path}.staminaCost", "Stamina cost must be finite and between zero and 100.");

        switch (effect.Kind)
        {
            case Wh40kClassSkillEffectKind.StatModifier:
                if (effect.Mechanic != Wh40kClassRuntimeMechanic.ProfileModifier)
                    Add(diagnostics, "invalid-stat-mechanic", $"{path}.mechanic", "Stat modifiers use the profileModifier runtime mechanic.");
                if (effect.Characteristic == null)
                    Add(diagnostics, "missing-characteristic", $"{path}.characteristic", "Enabled stat modifier requires a characteristic.");
                if (effect.Magnitude == 0)
                    Add(diagnostics, "zero-magnitude", $"{path}.magnitude", "Enabled stat modifier requires a non-zero magnitude.");
                if (effect.MaximumMagnitude <= 0 || Math.Abs((long) effect.Magnitude) > effect.MaximumMagnitude)
                    Add(diagnostics, "invalid-maximum-magnitude", $"{path}.maximumMagnitude", "Maximum magnitude must cap the base absolute magnitude.");
                if (effect.Action != null)
                    Add(diagnostics, "unexpected-action", $"{path}.action", "Stat modifier cannot declare an Action.");
                if (effect.Duration != TimeSpan.Zero || effect.MaximumDuration != TimeSpan.Zero)
                    Add(diagnostics, "unexpected-stat-duration", $"{path}.duration", "Profile stat modifiers do not own a duration.");
                if (effect.Safety != Wh40kClassEffectSafety.SelfOnly)
                    Add(diagnostics, "invalid-stat-safety", $"{path}.safety", "Profile stat modifiers must be self-only.");
                break;
            case Wh40kClassSkillEffectKind.GrantAction:
                if (effect.Action is not { } action)
                {
                    Add(diagnostics, "missing-action", $"{path}.action", "Enabled grantAction effect requires an Action prototype.");
                }
                if (effect.Characteristic != null)
                    Add(diagnostics, "unexpected-action-characteristic", $"{path}.characteristic", "GrantAction cannot modify a profile characteristic directly.");
                if (effect.Magnitude != 0 &&
                    (effect.MaximumMagnitude <= 0 || Math.Abs((long) effect.Magnitude) > effect.MaximumMagnitude))
                {
                    Add(diagnostics, "invalid-action-magnitude-cap", $"{path}.maximumMagnitude", "A non-zero action magnitude requires a cap at least as large as its base absolute magnitude.");
                }
                if (effect.Duration < TimeSpan.Zero || effect.MaximumDuration < TimeSpan.Zero)
                    Add(diagnostics, "negative-action-duration", $"{path}.duration", "Action durations cannot be negative.");
                if (effect.Duration == TimeSpan.Zero && effect.MaximumDuration > TimeSpan.Zero)
                    Add(diagnostics, "unused-action-duration-cap", $"{path}.maximumDuration", "A duration cap requires a positive base duration.");
                if (effect.Duration > TimeSpan.Zero && effect.MaximumDuration < effect.Duration)
                    Add(diagnostics, "invalid-action-duration-cap", $"{path}.maximumDuration", "Maximum duration must be at least the base duration.");
                break;
            case Wh40kClassSkillEffectKind.StatusReaction:
                if (effect.Mechanic == Wh40kClassRuntimeMechanic.PressureAmplifier &&
                    effect.Safety != Wh40kClassEffectSafety.NpcOnly)
                {
                    Add(diagnostics, "invalid-pressure-amplifier-safety", $"{path}.safety",
                        "Pressure amplifiers are NPC-only and never affect player bodies.");
                }
                break;
            case Wh40kClassSkillEffectKind.TargetMark:
                if (effect.Mechanic != Wh40kClassRuntimeMechanic.TargetMark)
                    Add(diagnostics, "invalid-mark-mechanic", $"{path}.mechanic", "Target marks use the targetMark runtime mechanic.");
                RequireTargetedShape(effect, path, diagnostics);
                break;
            case Wh40kClassSkillEffectKind.AreaCommand:
                if (effect.Range <= 0f || effect.MaximumTargets <= 0 || effect.Duration <= TimeSpan.Zero)
                    Add(diagnostics, "invalid-area-shape", path, "Area effects require positive range, duration, and target cap.");
                if (effect.Mechanic is Wh40kClassRuntimeMechanic.CommandAura or
                        Wh40kClassRuntimeMechanic.CommandStamina or Wh40kClassRuntimeMechanic.CommandBeacon &&
                    (effect.Range != Wh40kClassRuntimePolicy.OverseerCommandRadius ||
                     effect.Safety != Wh40kClassEffectSafety.AreaEffect))
                {
                    Add(diagnostics, "invalid-overseer-command-shape", path,
                        "Overseer commands require exactly ten tiles and areaEffect safety.");
                }
                break;
            case Wh40kClassSkillEffectKind.DeviceInteraction:
                if (effect.Range <= 0f || effect.Duration <= TimeSpan.Zero || effect.Safety != Wh40kClassEffectSafety.DeviceInteraction)
                    Add(diagnostics, "invalid-device-shape", path, "Device interactions require positive range/duration and deviceInteraction safety.");
                break;
            case Wh40kClassSkillEffectKind.NpcPressure:
                if (effect.Mechanic is not (Wh40kClassRuntimeMechanic.NpcPressure or
                    Wh40kClassRuntimeMechanic.AreaPressure or
                    Wh40kClassRuntimeMechanic.SuppressionMode or
                    Wh40kClassRuntimeMechanic.Distraction))
                    Add(diagnostics, "invalid-pressure-mechanic", $"{path}.mechanic", "NPC pressure effects require npcPressure, areaPressure, suppressionMode, or distraction mechanics.");
                if (effect.Range <= 0f || effect.Duration <= TimeSpan.Zero || effect.Safety != Wh40kClassEffectSafety.NpcOnly)
                    Add(diagnostics, "invalid-pressure-shape", path, "NPC pressure requires positive range/duration and npcOnly safety.");
                break;
        }
    }

    private static void RequireTargetedShape(
        Wh40kClassSkillEffectPrototype effect,
        string path,
        ICollection<Wh40kClassProgressionDiagnostic> diagnostics)
    {
        if (effect.Range <= 0f || effect.Duration <= TimeSpan.Zero || effect.MaximumTargets != 1)
            Add(diagnostics, "invalid-targeted-shape", path, "Targeted effects require positive range/duration and exactly one target.");
    }

    private static void ValidateNoPrerequisiteCycle(
        Wh40kClassSkillPrototype skill,
        IReadOnlyDictionary<string, Wh40kClassSkillPrototype> skills,
        ICollection<Wh40kClassProgressionDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { skill.ID };
        var current = skill;
        while (current.Prerequisite is { } prerequisite && skills.TryGetValue(prerequisite.Id, out current!))
        {
            if (seen.Add(current.ID))
                continue;

            Add(diagnostics, "prerequisite-cycle", $"wh40kClassSkill.{skill.ID}.prerequisite", "Prerequisite chain contains a cycle.");
            return;
        }
    }

    private static void ValidatePresentation(
        LocId name,
        LocId description,
        Robust.Shared.Utility.ResPath icon,
        string path,
        Func<LocId, bool> hasLocalization,
        Func<Robust.Shared.Utility.ResPath, bool>? hasResource,
        ICollection<Wh40kClassProgressionDiagnostic> diagnostics)
    {
        if (!hasLocalization(name))
            Add(diagnostics, "missing-name-localization", $"{path}.name", $"Localization '{name}' does not exist.");
        if (!hasLocalization(description))
            Add(diagnostics, "missing-description-localization", $"{path}.description", $"Localization '{description}' does not exist.");
        if (hasResource != null && !hasResource(icon))
            Add(diagnostics, "missing-icon", $"{path}.icon", $"Resource '{icon}' does not exist.");
    }

    private static void Add(
        ICollection<Wh40kClassProgressionDiagnostic> diagnostics,
        string code,
        string path,
        string message)
    {
        diagnostics.Add(new Wh40kClassProgressionDiagnostic(code, path, message));
    }
}

/// <summary>
/// Fails server startup when the production catalog is structurally invalid.
/// </summary>
public sealed partial class Wh40kClassProgressionValidationSystem : EntitySystem
{
    [Dependency] private ILocalizationManager _localization = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        var result = Wh40kClassProgressionValidator.Validate(
            _prototypes.EnumeratePrototypes<Wh40kCharacterClassPrototype>().ToArray(),
            _prototypes.EnumeratePrototypes<Wh40kClassSpecializationPrototype>().ToArray(),
            _prototypes.EnumeratePrototypes<Wh40kClassSkillPrototype>().ToArray(),
            _prototypes.EnumeratePrototypes<Wh40kClassSkillEffectPrototype>().ToArray(),
            locId => _localization.HasString(locId.Id),
            // Dedicated server packages intentionally exclude Resources/Textures. Icon existence is checked
            // by the integration catalog test, which runs with the complete source resource tree mounted.
            null,
            _prototypes.HasIndex<TagPrototype>,
            _prototypes.EnumeratePrototypes<Wh40kClassRarityModifierPrototype>().ToArray(),
            id => _prototypes.HasIndex<EntityPrototype>(id.Id),
            _prototypes.HasIndex<ItemRarityPrototype>);

        if (result.IsValid)
            return;

        var details = string.Join(Environment.NewLine,
            result.Diagnostics.Select(diagnostic => $"[{diagnostic.Code}] {diagnostic.Path}: {diagnostic.Message}"));
        throw new InvalidOperationException($"WH40K class progression catalog is invalid:{Environment.NewLine}{details}");
    }
}
