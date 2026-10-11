using System.Linq;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.Activities.Prototypes;
using Content.Shared._WH40K.Dialogue;
using Content.Shared._WH40K.Npcs;
using Content.Shared._WH40K.Progression;
using Content.Shared._WH40K.Quests;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.Access;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Quests;

public sealed record Wh40kContentDiagnostic(string Code, string Path, string Message);

/// <summary>Semantic checks in addition to normal YAML/prototype serialization validation.</summary>
public static class Wh40kQuestPrototypeValidator
{
    private static readonly string[] UnsafeComponents =
        ["Map", "MapGrid", "BecomesStation", "GhostRole", "GhostTakeoverAvailable", "Wh40kNpcIdentity"];

    public static List<Wh40kContentDiagnostic> ValidateStructure(Wh40kQuestPrototype quest)
    {
        var errors = new List<Wh40kContentDiagnostic>();
        void Add(string code, string path, string message) => errors.Add(new(code, path, message));
        if (quest.Version <= 0)
            Add("invalid-version", "version", "Definition version must be positive.");
        if ((quest.BranchGroup == null) != (quest.BranchChoice == null) || quest.BranchGroup?.Length is > 64 or 0 ||
            quest.BranchChoice?.Length is > 64 or 0 || quest.BranchGroup != null &&
            (quest.OwnerScope != Wh40kQuestOwnerScope.Account || quest.CompletionPolicy != Wh40kQuestCompletionPolicy.TurnIn))
            Add("invalid-branch", "branchGroup", "Branches require bounded group/choice keys, account ownership and explicit turn-in.");
        if (!Enum.IsDefined(quest.OwnerScope) || !Enum.IsDefined(quest.ObjectiveOrder) ||
            !Enum.IsDefined(quest.CompletionPolicy) || !Enum.IsDefined(quest.CrossRoundPolicy) ||
            !Enum.IsDefined(quest.RepeatPolicy))
            Add("invalid-policy", "policies", "Unknown contract policy.");
        if (quest.Lifetime is {} lifetime && lifetime <= TimeSpan.Zero)
            Add("invalid-lifetime", "lifetime", "Authored lifetime must be positive; omit for no expiry.");
        if (quest.Cooldown < TimeSpan.Zero ||
            (quest.RepeatPolicy == Wh40kQuestRepeatPolicy.Repeatable && quest.Cooldown == TimeSpan.Zero))
            Add("invalid-cooldown", "cooldown", "Repeatable quests require a positive cooldown.");
        if (quest.CompletionPolicy == Wh40kQuestCompletionPolicy.TurnIn && quest.Issuer == null)
            Add("missing-issuer", "issuer", "NPC turn-in requires an approved issuer anchor.");
        if (quest.CompletionPolicy == Wh40kQuestCompletionPolicy.Automatic &&
            quest.Objectives.Any(objective => objective.Kind == Wh40kQuestObjectiveKind.CollectItem))
            Add("collect-requires-turn-in", "completionPolicy", "Current possession must be rechecked at turn-in; use delivery for irreversible automatic completion.");
        if (quest.Objectives.Count == 0 || quest.Objectives.Count > 32)
            Add("invalid-objective-count", "objectives", "A contract needs 1–32 objectives.");
        if (quest.Rewards.Count > 32)
            Add("invalid-reward-count", "rewards", "Reward entries are bounded to 32.");
        if (quest.Prerequisites.Count > 32 || quest.Prerequisites.Distinct().Count() != quest.Prerequisites.Count)
            Add("invalid-prerequisites", "prerequisites", "Prerequisites must be unique and bounded to 32 entries.");

        var objectiveIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < quest.Objectives.Count; i++)
        {
            var objective = quest.Objectives[i];
            var path = $"objectives[{i}]";
            if (string.IsNullOrWhiteSpace(objective.Id) || !objectiveIds.Add(objective.Id))
                Add("invalid-objective-id", $"{path}.id", "Objective id is empty or duplicated.");
            if (objective.RequiredCount <= 0 || objective.RequiredCount > 10000)
                Add("invalid-amount", $"{path}.requiredCount", "Required count must be between 1 and 10000.");
            if (!Enum.IsDefined(objective.Kind) || !Enum.IsDefined(objective.Participation))
                Add("invalid-objective-kind", path, "Unknown objective or credit kind.");
            ValidateTargetShape(objective, path, errors);
            if (objective.Target.DurationSeconds is < 1 or > 3600 || objective.Target.CaseKey?.Length is > 64)
                Add("invalid-specialist-target", path, "Specialist duration/case keys are bounded.");
            if (objective.Kind is Wh40kQuestObjectiveKind.EscortEntity or Wh40kQuestObjectiveKind.DeliverEntity or
                Wh40kQuestObjectiveKind.RescueAndDeliver or Wh40kQuestObjectiveKind.CaptureAlive && objective.Target.Destination == null)
                Add("missing-destination", path, "Entity hand-off requires an approved destination anchor.");
            if (objective.Kind == Wh40kQuestObjectiveKind.CollectEvidence && string.IsNullOrWhiteSpace(objective.Target.CaseKey))
                Add("missing-case", path, "Evidence must have an approved case key.");
            if (objective.Kind is Wh40kQuestObjectiveKind.EscortEntity or Wh40kQuestObjectiveKind.CaptureAlive or
                Wh40kQuestObjectiveKind.ProtectTarget or Wh40kQuestObjectiveKind.StabilizeTarget &&
                (objective.Target.Kind != Wh40kQuestTargetKind.Entity || string.IsNullOrWhiteSpace(objective.Target.CaseKey)))
                Add("invalid-subject-target", path, "Bound subjects require one reviewed entity prototype and a case key.");
            if (objective.Kind is Wh40kQuestObjectiveKind.ProtectTarget or Wh40kQuestObjectiveKind.SurviveDuration &&
                objective.RequiredCount != objective.Target.DurationSeconds)
                Add("invalid-duration-count", path, "Timed objectives count eligible seconds and must match durationSeconds.");
            if (objective.Kind == Wh40kQuestObjectiveKind.PayDebtOrFee &&
                (objective.Target.SourceItem?.Id != "SpaceCash" || quest.Issuer == null || !objective.Target.Anchors.Contains(quest.Issuer.Value)))
                Add("invalid-fee-target", path, "Cash fees must name SpaceCash and the approved issuer intake.");
            if (quest.OwnerScope == Wh40kQuestOwnerScope.Party && (objective.Kind == Wh40kQuestObjectiveKind.CollectItem || objective.Kind.IsSubmission()))
                Add("party-body-custody", path, "Party work uses event objectives; per-account item custody and fees require account ownership.");
        }

        var rewardIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < quest.Rewards.Count; i++)
        {
            var reward = quest.Rewards[i];
            var path = $"rewards[{i}]";
            if (string.IsNullOrWhiteSpace(reward.EntryId) || !rewardIds.Add(reward.EntryId))
                Add("invalid-reward-id", $"{path}.entryId", "Reward entry id is empty or duplicated.");
            if (reward.Amount <= 0 || reward.Amount > 1000000)
                Add("invalid-reward-amount", $"{path}.amount", "Reward amount must be between 1 and 1000000.");
            var fields = (reward.Item != null ? 1 : 0) + (reward.ExperienceSource != null ? 1 : 0) +
                         (reward.Faction != null ? 1 : 0) + (reward.Flag != null ? 1 : 0) + (reward.Access != null ? 1 : 0);
            var valid = reward.Kind switch
            {
                Wh40kQuestRewardKind.Experience => reward.ExperienceSource != null && fields == 1,
                Wh40kQuestRewardKind.Item => reward.Item != null && fields == 1 && reward.Amount <= 1000,
                Wh40kQuestRewardKind.Currency => fields == 0,
                Wh40kQuestRewardKind.FactionReputation => reward.Faction != null && fields == 1,
                Wh40kQuestRewardKind.Flag => !string.IsNullOrWhiteSpace(reward.Flag) && reward.Flag.Length <= 64 && fields == 1 && reward.Amount == 1,
                Wh40kQuestRewardKind.Access => reward.Access != null && fields == 1 && reward.Amount == 1,
                _ => false,
            };
            if (!valid)
                Add("invalid-reward-shape", path, "Reward fields do not match its type.");
        }
        return errors;
    }

    private static void ValidateTargetShape(Wh40kQuestObjectiveDefinition objective, string path,
        List<Wh40kContentDiagnostic> errors)
    {
        var target = objective.Target;
        var branches = (target.Entity != null ? 1 : 0) +
                       (target.Tag != null || target.ApprovedPrototypes.Count > 0 ? 1 : 0) +
                       (target.Anchors.Count > 0 || target.Dialogue != null ? 1 : 0) +
                       (target.System != null && target.Kind != Wh40kQuestTargetKind.Activity || target.Sector != null || target.LocationKey != null || target.Position != null ? 1 : 0) +
                       (target.Activity != null || target.TerminalReason != null ? 1 : 0);
        var valid = target.Kind switch
        {
            Wh40kQuestTargetKind.Entity => target.Entity != null,
            Wh40kQuestTargetKind.Tag => target.Tag != null && target.ApprovedPrototypes.Count > 0 &&
                                       target.ApprovedPrototypes.Count <= 128 && target.ApprovedPrototypes.Distinct().Count() == target.ApprovedPrototypes.Count,
            Wh40kQuestTargetKind.NpcAnchor => target.Anchors.Count > 0 && target.Anchors.Count <= 64 &&
                                            target.Anchors.Distinct().Count() == target.Anchors.Count,
            Wh40kQuestTargetKind.Location => ValidateLocation(target),
            Wh40kQuestTargetKind.Activity => target.Activity != null,
            _ => false,
        };
        if (!valid || branches != 1)
            errors.Add(new("invalid-target", $"{path}.target", "Selector must contain exactly one valid typed target branch."));

        var compatible = objective.Kind switch
        {
            Wh40kQuestObjectiveKind.TalkToNpc => target.Kind == Wh40kQuestTargetKind.NpcAnchor && target.Dialogue != null,
            Wh40kQuestObjectiveKind.FactionDecision or Wh40kQuestObjectiveKind.PayDebtOrFee => target.Kind == Wh40kQuestTargetKind.NpcAnchor,
            Wh40kQuestObjectiveKind.ReachLocation or Wh40kQuestObjectiveKind.DiscoverLocation or
                Wh40kQuestObjectiveKind.InvestigateLocation => target.Kind == Wh40kQuestTargetKind.Location,
            Wh40kQuestObjectiveKind.CompleteActivity or Wh40kQuestObjectiveKind.RecoverActivityObjective =>
                target.Kind == Wh40kQuestTargetKind.Activity && target.TerminalReason != null,
            _ => target.Kind is Wh40kQuestTargetKind.Entity or Wh40kQuestTargetKind.Tag,
        };
        if (!compatible)
            errors.Add(new("incompatible-objective-target", $"{path}.target", "Objective does not support this selector type."));
        if (objective.Kind == Wh40kQuestObjectiveKind.TalkToNpc && objective.DistinctTargets &&
            objective.RequiredCount > target.Anchors.Count)
            errors.Add(new("impossible-distinct-count", path, "Distinct talk count exceeds the allowed anchor set."));
        if (target.TerminalReason is {} reason &&
            reason is not (KoronusActivityTerminalReason.Completed or KoronusActivityTerminalReason.Defeated or
                KoronusActivityTerminalReason.Contained or KoronusActivityTerminalReason.Evaded))
            errors.Add(new("invalid-terminal-reason", $"{path}.target.terminalReason", "Failure/cleanup cannot satisfy successful activity completion."));
    }

    private static bool ValidateLocation(Wh40kQuestTargetDefinition target)
    {
        if (!float.IsFinite(target.Radius) || target.Radius <= 0 || target.Radius > 1000)
            return false;
        return target.LocationKind switch
        {
            Wh40kQuestLocationKind.System => target.System != null && target.Sector == null && target.LocationKey == null && target.Position == null,
            Wh40kQuestLocationKind.Sector => target.Sector != null && target.System == null && target.LocationKey == null && target.Position == null,
            Wh40kQuestLocationKind.Grid or Wh40kQuestLocationKind.PointOfInterest => target.System != null && target.Sector == null &&
                !string.IsNullOrWhiteSpace(target.LocationKey) && target.Position == null,
            Wh40kQuestLocationKind.Coordinates => target.System != null && target.Sector == null && target.LocationKey == null &&
                target.Position is {} pos && float.IsFinite(pos.X) && float.IsFinite(pos.Y),
            _ => false,
        };
    }

    public static List<Wh40kContentDiagnostic> Validate(Wh40kQuestPrototype quest,
        IPrototypeManager prototypes, Func<string, bool> hasString)
    {
        var errors = ValidateStructure(quest);
        Localization(quest.Title, "title", hasString, errors);
        Localization(quest.Description, "description", hasString, errors);
        if (quest.Issuer is {} issuer)
            Reference(issuer, "issuer", prototypes, errors);
        foreach (var prerequisite in quest.Prerequisites)
            Reference(prerequisite, "prerequisites", prototypes, errors);

        for (var i = 0; i < quest.Objectives.Count; i++)
        {
            var objective = quest.Objectives[i];
            var path = $"objectives[{i}]";
            var target = objective.Target;
            Localization(objective.Text, $"{path}.text", hasString, errors);
            if (target.Entity is {} entity)
                Entity(entity, $"{path}.target.entity", prototypes, errors, objective.Kind == Wh40kQuestObjectiveKind.KillEntity);
            if (objective.Kind is Wh40kQuestObjectiveKind.EscortEntity or Wh40kQuestObjectiveKind.CaptureAlive or
                Wh40kQuestObjectiveKind.ProtectTarget or Wh40kQuestObjectiveKind.StabilizeTarget &&
                target.Entity is {} subject && prototypes.TryIndex(subject, out var subjectPrototype) &&
                (!subjectPrototype.Components.TryGetComponent("Wh40kQuestSubject", out var marker) || marker is not Wh40kQuestSubjectComponent caseMarker ||
                 caseMarker.CaseKey != target.CaseKey || !subjectPrototype.Components.ContainsKey("MobState") || subjectPrototype.Components.ContainsKey("Godmode")))
                errors.Add(new("unsafe-subject", path, "Mission subjects must be living, disposable, case-marked prototypes with no hub Godmode."));
            foreach (var approved in target.ApprovedPrototypes)
                Entity(approved, $"{path}.target.approvedPrototypes", prototypes, errors, objective.Kind == Wh40kQuestObjectiveKind.KillEntity);
            if (objective.Kind is Wh40kQuestObjectiveKind.CollectItem or Wh40kQuestObjectiveKind.DeliverItem or Wh40kQuestObjectiveKind.ExtractResource)
            {
                foreach (var item in target.ApprovedPrototypes.Concat(target.Entity is {} selected ? [selected] : []))
                    RequireItem(item, path, prototypes, errors);
            }
            if (target.Tag is {} tag)
            {
                Reference(tag, $"{path}.target.tag", prototypes, errors);
                foreach (var approved in target.ApprovedPrototypes)
                {
                    if (prototypes.TryIndex(approved, out var entityPrototype) &&
                        (!entityPrototype.Components.TryGetComponent("Tag", out var comp) ||
                         comp is not TagComponent tags || !HasAuthoredTag(tags, tag)))
                        errors.Add(new("missing-target-tag", path, $"Approved target '{approved}' does not have tag '{tag}'."));
                }
            }
            foreach (var anchor in target.Anchors)
                Reference(anchor, $"{path}.target.anchors", prototypes, errors);
            if (target.Dialogue is {} dialogue) Reference(dialogue, $"{path}.target.dialogue", prototypes, errors);
            if (target.System is {} system) Reference(system, $"{path}.target.system", prototypes, errors);
            if (target.Sector is {} sector) Reference(sector, $"{path}.target.sector", prototypes, errors);
            if (target.Activity is {} activity) Reference(activity, $"{path}.target.activity", prototypes, errors);
            if (target.Destination is {} destination) Reference(destination, $"{path}.target.destination", prototypes, errors);
            if (target.SourceItem is {} sourceItem)
            {
                Entity(sourceItem, $"{path}.target.sourceItem", prototypes, errors);
                RequireItem(sourceItem, path, prototypes, errors);
            }
        }

        for (var i = 0; i < quest.Rewards.Count; i++)
        {
            var reward = quest.Rewards[i];
            var path = $"rewards[{i}]";
            if (reward.Item is {} item)
            {
                Entity(item, $"{path}.item", prototypes, errors);
                RequireItem(item, path, prototypes, errors);
            }
            if (reward.ExperienceSource is {} xp)
            {
                Reference(xp, $"{path}.experienceSource", prototypes, errors);
                if (prototypes.TryIndex(xp, out var source) && source.SourceType != Wh40kExperienceSourceType.Mission)
                    errors.Add(new("invalid-xp-source", path, "Quest XP requires a Mission source."));
            }
            if (reward.Faction is {} faction) Reference(faction, $"{path}.faction", prototypes, errors);
            if (reward.Access is {} access) Reference(access, $"{path}.access", prototypes, errors);
        }
        return errors;
    }

    private static bool HasAuthoredTag(TagComponent component, ProtoId<TagPrototype> tag)
    {
        // Read the prototype's tag collection without mutating its component template.
        IReadOnlySet<ProtoId<TagPrototype>> tags = component.Tags;
        return tags.Contains(tag);
    }

    private static void RequireItem(EntProtoId id, string path, IPrototypeManager prototypes, List<Wh40kContentDiagnostic> errors)
    {
        if (prototypes.TryIndex(id, out var entity) && !entity.Components.ContainsKey("Item"))
            errors.Add(new("non-item-target", path, $"Entity '{id}' is not a carryable item."));
    }

    public static List<Wh40kContentDiagnostic> ValidatePrerequisites(IReadOnlyDictionary<string, Wh40kQuestPrototype> definitions)
    {
        var errors = new List<Wh40kContentDiagnostic>();
        var active = new HashSet<string>(StringComparer.Ordinal);
        var results = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (active.Contains(id)) return true;
            if (results.TryGetValue(id, out var cached)) return cached;
            if (!definitions.TryGetValue(id, out var definition)) return false;
            active.Add(id);
            var cyclic = definition.Prerequisites.Any(prerequisite => Visit(prerequisite.Id));
            active.Remove(id);
            results[id] = cyclic;
            return cyclic;
        }
        foreach (var id in definitions.Keys)
        {
            if (Visit(id))
                errors.Add(new("prerequisite-cycle", $"{id}.prerequisites", "This quest depends on a cyclic prerequisite chain."));
        }
        return errors;
    }

    public static void Localization(LocId id, string path, Func<string, bool> hasString, List<Wh40kContentDiagnostic> errors)
    {
        if (string.IsNullOrWhiteSpace(id.Id) || !hasString(id.Id))
            errors.Add(new("missing-localization", path, $"Missing localization '{id}'."));
    }

    public static void Reference<T>(ProtoId<T> id, string path, IPrototypeManager prototypes,
        List<Wh40kContentDiagnostic> errors) where T : class, IPrototype
    {
        if (string.IsNullOrWhiteSpace(id.Id) || !prototypes.HasIndex(id))
            errors.Add(new("missing-prototype", path, $"Unknown {typeof(T).Name} '{id}'."));
    }

    public static void Entity(EntProtoId id, string path, IPrototypeManager prototypes,
        List<Wh40kContentDiagnostic> errors, bool requireMob = false)
    {
        if (string.IsNullOrWhiteSpace(id.Id) || !prototypes.TryIndex(id, out var entity))
        {
            errors.Add(new("missing-prototype", path, $"Unknown entity '{id}'."));
            return;
        }
        if (entity.Abstract || UnsafeComponents.Any(entity.Components.ContainsKey) ||
            (requireMob && (!entity.Components.ContainsKey("MobState") || !entity.Components.ContainsKey("HTN") || entity.Components.ContainsKey("Godmode"))))
            errors.Add(new("unsafe-entity-target", path, $"Entity '{id}' is not an approved objective/reward shape."));
    }
}
