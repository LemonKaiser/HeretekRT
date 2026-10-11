using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared._WH40K.Quests;

namespace Content.Server._WH40K.Quests;

public static class Wh40kQuestSnapshotCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };

    public static string Serialize(Wh40kQuestContractSnapshot snapshot)
    {
        Validate(snapshot);
        var json = JsonSerializer.Serialize(snapshot, Options);
        if (json.Length > 131072) throw new ArgumentException("Quest snapshot exceeds its storage bound.");
        return json;
    }

    public static Wh40kQuestContractSnapshot Deserialize(string json)
    {
        if (json.Length > 131072) throw new ArgumentException("Quest snapshot exceeds its storage bound.");
        var snapshot = JsonSerializer.Deserialize<Wh40kQuestContractSnapshot>(json, Options)
            ?? throw new ArgumentException("Missing quest snapshot.");
        Validate(snapshot);
        return snapshot;
    }

    public static bool TryDeserialize(string json, out Wh40kQuestContractSnapshot? snapshot)
    {
        try { snapshot = Deserialize(json); return true; }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { snapshot = null; return false; }
    }

    [SuppressMessage("Usage", "RA0039", Justification = "Reconstruct isolated schema data for the existing pure validator; never register it as a prototype.")]
    private static void Validate(Wh40kQuestContractSnapshot value)
    {
        if (value.SchemaVersion != 1 || value.GeneratorVersion <= 0 || !value.Owner.IsValid ||
            string.IsNullOrWhiteSpace(value.DefinitionId) || value.DefinitionId.Length > 128 ||
            value.AcceptedAt.Offset != TimeSpan.Zero || value.Deadline is {} deadline &&
            (deadline.Offset != TimeSpan.Zero || deadline <= value.AcceptedAt) ||
            value.Objectives.IsDefault || value.Rewards.IsDefault || value.Prerequisites.IsDefault || value.Recipients.IsDefault ||
            value.Recipients.Length is <= 0 or > 32 || value.Recipients.Any(id => id == Guid.Empty) ||
            value.Recipients.Distinct().Count() != value.Recipients.Length ||
            value.Owner.Scope == Wh40kQuestOwnerScope.Account && (value.Recipients.Length != 1 || value.Recipients[0] != value.Owner.Id))
            throw new ArgumentException("Unsupported or malformed frozen quest contract.");
        var definition = RestoreDefinition(value);
        if (Wh40kQuestPrototypeValidator.ValidateStructure(definition).Count > 0 ||
            value.Objectives.Any(o => o.Id.Length > 64) || value.Rewards.Any(r => r.EntryId.Length > 64) ||
            value.Prerequisites.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128))
            throw new ArgumentException("Frozen quest contains invalid objectives, rewards or policies.");
    }

    [SuppressMessage("Usage", "RA0039", Justification = "Isolated schema data for validation, never registered or used as mutable live content.")]
    internal static Wh40kQuestPrototype RestoreDefinition(Wh40kQuestContractSnapshot value)
        => new()
        {
            Title = value.Title, Description = value.Description,
            Version = value.DefinitionVersion, OwnerScope = value.Owner.Scope, ObjectiveOrder = value.Order,
            CompletionPolicy = value.Completion, CrossRoundPolicy = value.RoundPolicy, RepeatPolicy = value.Repeat,
            Cooldown = value.Cooldown, Issuer = value.Issuer == null ? null : new(value.Issuer),
            BranchGroup = value.BranchGroup, BranchChoice = value.BranchChoice,
            Prerequisites = value.Prerequisites.Select(id => new Robust.Shared.Prototypes.ProtoId<Wh40kQuestPrototype>(id)).ToList(),
            Lifetime = value.Deadline - value.AcceptedAt,
            Objectives = value.Objectives.Select(o => new Wh40kQuestObjectiveDefinition
            {
                Id = o.Id, Kind = o.Kind, RequiredCount = o.RequiredCount, Text = o.Text,
                Participation = o.Participation, DistinctTargets = o.DistinctTargets,
                Target = new()
                {
                    Kind = o.Target.Kind, Entity = o.Target.Entity == null ? null : new(o.Target.Entity),
                    Tag = o.Target.Tag == null ? null : new(o.Target.Tag),
                    ApprovedPrototypes = o.Target.ApprovedPrototypes.Select(id => new Robust.Shared.Prototypes.EntProtoId(id)).ToList(),
                    Anchors = o.Target.Anchors.Select(id => new Robust.Shared.Prototypes.ProtoId<Content.Shared._WH40K.Npcs.Wh40kNpcAnchorPrototype>(id)).ToList(),
                    Dialogue = o.Target.Dialogue == null ? null : new(o.Target.Dialogue),
                    LocationKind = o.Target.LocationKind, System = o.Target.System == null ? null : new(o.Target.System),
                    Sector = o.Target.Sector == null ? null : new(o.Target.Sector), LocationKey = o.Target.LocationKey,
                    Position = o.Target.Position, Radius = o.Target.Radius,
                    Activity = o.Target.Activity == null ? null : new(o.Target.Activity), TerminalReason = o.Target.TerminalReason,
                    Destination = o.Target.Destination == null ? null : new(o.Target.Destination),
                    SourceItem = o.Target.SourceItem == null ? null : new(o.Target.SourceItem),
                    CaseKey = o.Target.CaseKey, DurationSeconds = o.Target.DurationSeconds,
                },
            }).ToList(),
            Rewards = value.Rewards.Select(r => new Wh40kQuestRewardDefinition
            {
                EntryId = r.EntryId, Kind = r.Kind, Amount = r.Amount, Item = r.Item == null ? null : new(r.Item),
                ExperienceSource = r.ExperienceSource == null ? null : new(r.ExperienceSource),
                Faction = r.Faction == null ? null : new(r.Faction), Flag = r.Flag, Access = r.Access == null ? null : new(r.Access),
            }).ToList(),
        };
}
