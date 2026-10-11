using System.Linq;
using Content.Server._WH40K.Npcs;
using Content.Shared._WH40K.Merchant;
using Content.Shared._WH40K.Npcs;
using Content.Shared._WH40K.Quests;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Quests;

/// <summary>Startup/hot-reload diagnostics. Invalid content is not exposed as an available definition.</summary>
public sealed partial class Wh40kQuestContentSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ILocalizationManager _localization = default!;
    private readonly Dictionary<string, IReadOnlyList<Wh40kContentDiagnostic>> _diagnostics = new();
    public IReadOnlyDictionary<string, IReadOnlyList<Wh40kContentDiagnostic>> Diagnostics => _diagnostics;

    public override void Initialize()
    {
        base.Initialize();
        _prototypes.PrototypesReloaded += OnReload;
        ValidateContent();
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnReload;
        base.Shutdown();
    }

    private void OnReload(PrototypesReloadedEventArgs args) => ValidateContent();

    public bool IsValidQuest(ProtoId<Wh40kQuestPrototype> id)
        => _prototypes.HasIndex(id) && _diagnostics.TryGetValue($"quest:{id}", out var errors) && errors.Count == 0;

    public bool IsValidAnchor(ProtoId<Wh40kNpcAnchorPrototype> id)
        => _diagnostics.TryGetValue($"anchor:{id}", out var errors) && errors.Count == 0;

    public bool IsValidPool(ProtoId<Wh40kQuestPoolPrototype> id)
        => _prototypes.HasIndex(id) && _diagnostics.TryGetValue($"pool:{id}", out var errors) && errors.Count == 0;

    public bool IsCompatibleContract(Wh40kQuestContractSnapshot snapshot)
    {
        if (!IsValidQuest(snapshot.DefinitionId)) return false;
        var frozen = Wh40kQuestSnapshotCodec.RestoreDefinition(snapshot);
        return Wh40kQuestPrototypeValidator.Validate(frozen, _prototypes, _localization.HasString).Count == 0 &&
            frozen.Objectives.SelectMany(o => o.Target.Anchors.Concat(o.Target.Destination is {} dest ? [dest] : [])).Concat(frozen.Issuer is {} issuer ? [issuer] : [])
                .All(IsValidAnchor);
    }

    public void ValidateContent()
    {
        _diagnostics.Clear();
        var quests = _prototypes.EnumeratePrototypes<Wh40kQuestPrototype>().ToDictionary(quest => quest.ID);
        foreach (var faction in _prototypes.EnumeratePrototypes<Wh40kNpcFactionPrototype>())
        {
            var errors = new List<Wh40kContentDiagnostic>();
            Wh40kQuestPrototypeValidator.Localization(faction.Name, "name", _localization.HasString, errors);
            Record($"faction:{faction.ID}", errors);
        }
        foreach (var profile in _prototypes.EnumeratePrototypes<Wh40kNpcProfilePrototype>())
        {
            var errors = Wh40kNpcPrototypeValidator.Validate(profile, _prototypes, _localization.HasString);
            if (profile.Faction is {} faction && _diagnostics.TryGetValue($"faction:{faction}", out var factionErrors) && factionErrors.Count > 0)
                errors.Add(new("invalid-faction", "faction", "NPC references invalid faction content."));
            Record($"profile:{profile.ID}", errors);
        }
        var anchors = _prototypes.EnumeratePrototypes<Wh40kNpcAnchorPrototype>().ToArray();
        foreach (var anchor in anchors)
        {
            var errors = Wh40kNpcPrototypeValidator.ValidateAnchor(anchor, anchors, _prototypes);
            if (_diagnostics.TryGetValue($"profile:{anchor.Profile}", out var profileErrors) && profileErrors.Count > 0)
                errors.Add(new("invalid-profile", "profile", "Anchor references invalid NPC content."));
            Record($"anchor:{anchor.ID}", errors);
        }
        foreach (var quest in quests.Values)
        {
            var errors = Wh40kQuestPrototypeValidator.Validate(quest, _prototypes, _localization.HasString);
            foreach (var anchor in quest.Objectives.SelectMany(objective => objective.Target.Anchors.Concat(objective.Target.Destination is {} dest ? [dest] : [])).Concat(quest.Issuer is {} issuer ? [issuer] : []))
            {
                if (!IsValidAnchor(anchor)) errors.Add(new("invalid-anchor", "anchors", $"Anchor '{anchor}' is unavailable."));
            }
            if (quest.Issuer is {} source && _prototypes.TryIndex(source, out var sourceAnchor) &&
                _prototypes.TryIndex(sourceAnchor.Profile, out var sourceProfile) &&
                (!sourceProfile.Services.Contains(Wh40kNpcService.Quests) ||
                 quest.Objectives.Any(objective => !sourceProfile.AllowedObjectives.Contains(objective.Kind))))
                errors.Add(new("incompatible-issuer", "issuer", "Issuer cannot offer these objective kinds."));
            foreach (var objective in quest.Objectives.Where(objective => objective.Kind == Wh40kQuestObjectiveKind.TalkToNpc))
            {
                foreach (var key in objective.Target.Anchors)
                {
                    if (_prototypes.TryIndex(key, out var targetAnchor) &&
                        _prototypes.TryIndex(targetAnchor.Profile, out var targetProfile) &&
                        (targetProfile.Kind != Wh40kNpcKind.Special || targetProfile.Dialogue != objective.Target.Dialogue))
                        errors.Add(new("incompatible-talk-target", objective.Id,
                            "Talk credit requires a special NPC with the approved dialogue; ambient NPCs cannot grant credit."));
                }
            }
            Record($"quest:{quest.ID}", errors);
        }
        foreach (var cycle in Wh40kQuestPrototypeValidator.ValidatePrerequisites(quests))
        {
            var id = cycle.Path[..^".prerequisites".Length];
            Append($"quest:{id}", cycle);
        }
        // Propagate invalid dependencies until stable, including indirect dependents.
        for (var pass = 0; pass < quests.Count; pass++)
        {
            var changed = false;
            foreach (var quest in quests.Values)
            {
                if (IsValidQuest(quest.ID) && quest.Prerequisites.Any(prerequisite => !IsValidQuest(prerequisite)))
                {
                    Append($"quest:{quest.ID}", new("invalid-prerequisite", "prerequisites", "A prerequisite is unavailable."));
                    changed = true;
                }
            }
            if (!changed) break;
        }
        foreach (var pool in _prototypes.EnumeratePrototypes<Wh40kQuestPoolPrototype>())
        {
            var errors = new List<Wh40kContentDiagnostic>();
            if (pool.Entries.Where(x => _prototypes.HasIndex(x.Quest)).Select(x => _prototypes.Index(x.Quest).OwnerScope).Distinct().Count() > 1)
                errors.Add(new("mixed-owner-pool", "entries", "A pool must have one explicit owner scope."));
            if (!IsValidAnchor(pool.Source)) errors.Add(new("invalid-source", "source", "Offer source is unavailable."));
            if (pool.OfferCount is <= 0 or > 32 || pool.ActiveLimit is <= 0 or > 5 ||
                pool.HistoryLimit is <= 0 or > 128 || pool.RefreshInterval <= TimeSpan.Zero || pool.Entries.Count is <= 0 or > 128)
                errors.Add(new("invalid-pool-limits", "limits", "Offer generation bounds are invalid."));
            var seen = new HashSet<ProtoId<Wh40kQuestPrototype>>();
            foreach (var entry in pool.Entries)
            {
                if (!seen.Add(entry.Quest) || entry.Weight is <= 0 or > 10000 || !IsValidQuest(entry.Quest))
                    errors.Add(new("invalid-pool-entry", "entries", "Duplicate/unavailable quest or invalid weight."));
                if (_prototypes.TryIndex(entry.Quest, out var quest) && quest.Issuer != pool.Source)
                    errors.Add(new("pool-issuer-mismatch", "entries", "Pool cannot offer another issuer's contract."));
            }
            Record($"pool:{pool.ID}", errors);
        }
        foreach (var merchant in _prototypes.EnumeratePrototypes<EntityPrototype>().Where(entity => !entity.Abstract &&
                     (entity.ID.StartsWith("MobWH40KFootfall", StringComparison.Ordinal) || entity.ID == "MobQueenChrysalisImperiumDialogueFizzo")))
            if (merchant.Components.TryGetComponent("Merchant", out var component) && component is MerchantComponent stock)
                Record("merchant:" + merchant.ID, Wh40kNpcPrototypeValidator.ValidateMerchant(stock, _prototypes, _localization.HasString));
    }

    private void Record(string key, List<Wh40kContentDiagnostic> errors)
    {
        _diagnostics[key] = errors.AsReadOnly();
        foreach (var error in errors) Log.Warning($"{key}: {error.Code} at {error.Path}: {error.Message}");
    }

    private void Append(string key, Wh40kContentDiagnostic error)
    {
        var errors = _diagnostics[key].ToList();
        errors.Add(error);
        Record(key, errors);
    }
}
