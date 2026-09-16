using Content.Server._WH40K.Activities.Components;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.Shuttles.Systems;
using Content.Server.GameTicking.Rules;
using System.Numerics;
using System.Linq;
using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.Activities.Prototypes;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Activities;

/// <summary>
/// Authoritative director for Koronus sector activity. Executors may materialize only explicitly
/// owned roots and NPCs after the director accepts a safe transition; it never owns a player,
/// player ship, map, reward or player role. A disposable activity grid is explicitly marked and
/// is evacuated before deletion.
/// </summary>
public sealed class KoronusActivityDirectorSystem : GameRuleSystem<KoronusActivityDirectorComponent>
{
    private static readonly TimeSpan DayProfileDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan MinimumLifetime = TimeSpan.FromMinutes(10);
    private const int MaximumHistoryEntries = 768;
    private const int DiversityHistoryWindow = 6;
    private const int SystemHistoryWindow = 4;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private KoronusSectorResidencySystem _residency = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DockingSystem _docking = default!;

    private EntityUid? _activeDirector;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    protected override void Started(
        EntityUid uid,
        KoronusActivityDirectorComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryComp(uid, out KoronusSectorRuleComponent? sectorRule))
            return;

        component.Active = true;
        component.Sector = sectorRule.Sector;
        component.RoundSeed = _random.Next();
        component.DayIndex = 0;
        component.DayFocus = SelectDayFocus(component.RoundSeed, component.DayIndex);
        component.NextDayProfileAt = _timing.CurTime + DayProfileDuration;
        component.NextInstanceId = 1;
        component.NextSpawnAt = _timing.CurTime;
        component.Markers.Clear();
        component.Instances.Clear();
        component.History.Clear();
        _activeDirector = uid;

        EnsureMinimumPopulation(component);
        ScheduleNextSpawn(component);
        RebuildPresentation(component);
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_activeDirector is not { } uid ||
            !TryComp(uid, out KoronusActivityDirectorComponent? component) ||
            !component.Active)
        {
            return;
        }

        RollDayProfile(component);

        var presentationChanged = ExpireActivities(component);
        if (EnsureMinimumPopulation(component))
            presentationChanged = true;

        if (_timing.CurTime >= component.NextSpawnAt)
        {
            if (component.Instances.Count < KoronusActivityRuntimePolicy.MaximumActiveActivities &&
                TryCreateActivity(component, out _))
            {
                presentationChanged = true;
            }

            ScheduleNextSpawn(component);
        }

        if (!presentationChanged)
            return;

        RebuildPresentation(component);
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
    }

    /// <summary>
    /// Returns a BUI-safe snapshot. The client cannot use a marker identifier as an authority token.
    /// </summary>
    public List<KoronusActivityMarkerState> GetMarkers()
    {
        if (!TryGetActiveComponent(out var component))
            return [];

        return [.. component.Markers];
    }

    /// <summary>
    /// Returns the active dossiers followed by a bounded terminal journal. No coordinate, entity,
    /// reward, or participant data leaves the server through this method.
    /// </summary>
    public List<KoronusActivityDossierState> GetDossiers()
    {
        if (!TryGetActiveComponent(out var component))
            return [];

        var dossiers = new List<KoronusActivityDossierState>();
        foreach (var instance in component.Instances)
        {
            if (!_prototypes.TryIndex<KoronusActivityTemplatePrototype>(instance.TemplateId, out var template))
                continue;

            dossiers.Add(CreateDossier(instance, template, null));
        }

        foreach (var history in component.History.AsEnumerable().Reverse())
        {
            if (!_prototypes.TryIndex<KoronusActivityTemplatePrototype>(history.TemplateId, out var template))
                continue;

            dossiers.Add(new KoronusActivityDossierState(
                history.InstanceId,
                history.SystemId,
                history.RouteFromSystem,
                history.RouteToSystem,
                template.Family,
                template.Lane,
                KoronusActivityState.Archived,
                template.Risk,
                template.Reliability,
                template.ThreatFamily,
                template.ThreatLevel,
                GetTerminalThreatState(template, history.Reason),
                history.Reason,
                template.TitleLocId,
                template.SummaryLocId,
                history.OccurredAt));
        }

        return dossiers;
    }

    /// <summary>
    /// The only external resolution API for the current data-only stage. It is intentionally
    /// idempotent: an archived or unknown instance cannot produce a second terminal effect.
    /// </summary>
    public bool TryResolve(long instanceId, KoronusActivityTerminalReason reason)
    {
        if (reason is not (KoronusActivityTerminalReason.Completed or KoronusActivityTerminalReason.Abandoned) ||
            !TryGetActiveComponent(out var component))
        {
            return false;
        }

        var instance = component.Instances.FirstOrDefault(candidate => candidate.Id == instanceId);
        if (instance == null || !Archive(component, instance, reason))
            return false;

        EnsureMinimumPopulation(component);
        RebuildPresentation(component);
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
        return true;
    }

    /// <summary>
    /// Registers exactly one audited root objective and moves an activity from its marker state to
    /// engaged. The caller has already proved that a living player reached the target map.
    /// </summary>
    public bool TryBeginObjective(
        long instanceId,
        EntityUid objective,
        KoronusActivityExecutionKind execution,
        bool requiresSystemLease)
    {
        if (!TryGetActiveComponent(out var component) ||
            !TryComp<KoronusActivityOwnedComponent>(objective, out var owned) ||
            !TryComp<KoronusActivityObjectiveComponent>(objective, out var objectiveComponent) ||
            owned.InstanceId != instanceId ||
            objectiveComponent.InstanceId != instanceId ||
            objectiveComponent.Execution != execution)
        {
            return false;
        }

        var instance = component.Instances.FirstOrDefault(candidate => candidate.Id == instanceId);
        if (instance == null ||
            instance.State != KoronusActivityState.Available ||
            !KoronusActivityLifecycle.CanTransition(instance.State, KoronusActivityState.Engaged))
        {
            return false;
        }

        if (requiresSystemLease && !_residency.BeginActivityLease(instance.SystemId, instance.Id))
            return false;

        instance.State = KoronusActivityState.Engaged;
        instance.Objective = objective;
        instance.HasSystemLease = requiresSystemLease;
        instance.LeaseStartedAt = requiresSystemLease ? _timing.CurTime : null;
        RebuildPresentation(component);
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
        return true;
    }

    /// <summary>
    /// Completes only the currently registered physical objective. A stale interaction cannot
    /// complete an archived, replaced, or unrelated activity.
    /// </summary>
    public bool TryCompleteObjective(long instanceId, EntityUid objective)
    {
        return TryFinalizeObjective(instanceId, objective, preserveObjective: false);
    }

    /// <summary>
    /// Completes a stage-two recovery only after its item has reached a living player's hand. The
    /// item is deliberately detached from activity cleanup so it remains a physical record for a
    /// later redemption stage; it has no reward or special authority by itself.
    /// </summary>
    public bool TryCompleteExtractedObjective(long instanceId, EntityUid objective)
    {
        if (!TryComp<KoronusActivityObjectiveComponent>(objective, out var objectiveComponent) ||
            !KoronusActivityRuntimePolicy.IsSetPieceExecution(objectiveComponent.Execution))
        {
            return false;
        }

        return TryFinalizeObjective(instanceId, objective, preserveObjective: true);
    }

    private bool TryFinalizeObjective(long instanceId, EntityUid objective, bool preserveObjective)
    {
        if (!TryGetActiveComponent(out var component))
            return false;

        var instance = component.Instances.FirstOrDefault(candidate => candidate.Id == instanceId);
        if (instance == null ||
            instance.State != KoronusActivityState.Engaged ||
            instance.Objective != objective ||
            !KoronusActivityLifecycle.CanTransition(instance.State, KoronusActivityState.Resolving))
        {
            return false;
        }

        instance.State = KoronusActivityState.Resolving;
        if (!Archive(component, instance, KoronusActivityTerminalReason.Completed,
                preserveObjective ? objective : null))
            return false;

        if (preserveObjective && Exists(objective))
        {
            RemComp<KoronusActivityOwnedComponent>(objective);
            RemComp<KoronusActivityObjectiveComponent>(objective);
        }

        EnsureMinimumPopulation(component);
        RebuildPresentation(component);
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
        return true;
    }

    /// <summary>
    /// Registers one hostile anchor after its root and every NPC passed the activity audits. The
    /// anchor is deliberately the only objective identity; hostile entities are cleanup-owned,
    /// never a second source of progress.
    /// </summary>
    public bool TryBeginThreat(
        long instanceId,
        EntityUid anchor,
        KoronusActivityExecutionKind execution,
        bool requiresSystemLease)
    {
        if (!KoronusActivityRuntimePolicy.IsHostileExecution(execution) ||
            !TryGetActiveComponent(out var component) ||
            !TryComp<KoronusActivityOwnedComponent>(anchor, out var owned) ||
            !TryComp<KoronusActivityThreatAnchorComponent>(anchor, out var threatAnchor) ||
            owned.InstanceId != instanceId ||
            threatAnchor.InstanceId != instanceId ||
            threatAnchor.Execution != execution)
        {
            return false;
        }

        var instance = component.Instances.FirstOrDefault(candidate => candidate.Id == instanceId);
        if (instance == null ||
            instance.State != KoronusActivityState.Available ||
            !KoronusActivityLifecycle.CanTransition(instance.State, KoronusActivityState.Engaged))
        {
            return false;
        }

        if (requiresSystemLease && !_residency.BeginActivityLease(instance.SystemId, instance.Id))
            return false;

        instance.State = KoronusActivityState.Engaged;
        instance.Objective = anchor;
        instance.HasSystemLease = requiresSystemLease;
        instance.LeaseStartedAt = requiresSystemLease ? _timing.CurTime : null;
        instance.ThreatState = KoronusActivityThreatState.Active;
        instance.LastThreatParticipantAt = _timing.CurTime;
        RebuildPresentation(component);
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
        return true;
    }

    /// <summary>
    /// Resolves a hostile contact exactly once after its own anchor was reached. Defeated is
    /// used for a cleared group, Contained for an alternate anchor interaction; neither creates a
    /// reward or a child encounter.
    /// </summary>
    public bool TryCompleteThreat(long instanceId, EntityUid anchor, KoronusActivityTerminalReason reason)
    {
        if (reason is not (KoronusActivityTerminalReason.Defeated or KoronusActivityTerminalReason.Contained) ||
            !TryGetActiveComponent(out var component))
        {
            return false;
        }

        var instance = component.Instances.FirstOrDefault(candidate => candidate.Id == instanceId);
        if (instance == null ||
            instance.State != KoronusActivityState.Engaged ||
            instance.Objective != anchor ||
            !KoronusActivityLifecycle.CanTransition(instance.State, KoronusActivityState.Resolving))
        {
            return false;
        }

        instance.State = KoronusActivityState.Resolving;
        instance.ThreatState = reason == KoronusActivityTerminalReason.Defeated
            ? KoronusActivityThreatState.Defeated
            : KoronusActivityThreatState.Contained;
        if (!Archive(component, instance, reason))
            return false;

        EnsureMinimumPopulation(component);
        RebuildPresentation(component);
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
        return true;
    }

    /// <summary>
    /// Fails a physical activity without granting any reward. This is used only for authoritative
    /// executor failures, such as a missing owned target or an impossible safe spawn band.
    /// </summary>
    public bool TryTerminateObjective(long instanceId, KoronusActivityTerminalReason reason)
    {
        if (reason is not (KoronusActivityTerminalReason.TargetUnreachable or
            KoronusActivityTerminalReason.OwnedEntityMissing or
            KoronusActivityTerminalReason.ContentAuditFailed or
            KoronusActivityTerminalReason.SafetyViolation) ||
            !TryGetActiveComponent(out var component))
        {
            return false;
        }

        var instance = component.Instances.FirstOrDefault(candidate => candidate.Id == instanceId);
        if (instance == null ||
            instance.State is not (KoronusActivityState.Available or KoronusActivityState.Engaged) ||
            !Archive(component, instance, reason))
        {
            return false;
        }

        EnsureMinimumPopulation(component);
        RebuildPresentation(component);
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
        return true;
    }

    /// <summary>
    /// Ends a hostile contact without a reward. This is the only path for leash, participant,
    /// safety and ownership failures; the same archive path removes every matching owned entity.
    /// </summary>
    public bool TryTerminateThreat(long instanceId, KoronusActivityTerminalReason reason)
    {
        if (!KoronusActivityRuntimePolicy.IsThreatTerminalReason(reason) ||
            !TryGetActiveComponent(out var component))
        {
            return false;
        }

        var instance = component.Instances.FirstOrDefault(candidate => candidate.Id == instanceId);
        if (instance == null ||
            instance.State is not (KoronusActivityState.Available or KoronusActivityState.Engaged) ||
            !Archive(component, instance, reason))
        {
            return false;
        }

        EnsureMinimumPopulation(component);
        RebuildPresentation(component);
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
        return true;
    }

    public bool TryGetStatus(out KoronusActivityDirectorStatus status)
    {
        if (!TryGetActiveComponent(out var component))
        {
            status = default;
            return false;
        }

        status = new KoronusActivityDirectorStatus(
            component.RoundSeed,
            component.DayIndex,
            component.DayFocus,
            component.NextSpawnAt,
            component.Instances.Count,
            component.History.Count);
        return true;
    }

    public List<KoronusActivityRuntimeInstance> GetActiveInstances()
    {
        return TryGetActiveComponent(out var component) ? [.. component.Instances] : [];
    }

    /// <summary>
    /// Aggregates bounded, read-only diagnostics for a seven-day round. The monitor deliberately
    /// receives no mutating API, player identities, map coordinates, or reward information.
    /// </summary>
    public KoronusActivityLongRunStatus GetLongRunStatus()
    {
        var hasDirector = TryGetActiveComponent(out var component);
        var activeIds = hasDirector
            ? component.Instances.Select(instance => instance.Id).ToHashSet()
            : [];
        var ownedEntities = 0;
        var ownedGrids = 0;
        var ownedNpcs = 0;
        var orphanCandidates = 0;
        var owned = EntityQueryEnumerator<KoronusActivityOwnedComponent>();
        while (owned.MoveNext(out _, out var marker))
        {
            if (!activeIds.Contains(marker.InstanceId))
            {
                orphanCandidates++;
                continue;
            }

            switch (marker.Kind)
            {
                case KoronusActivityOwnedKind.Grid:
                    ownedGrids++;
                    break;
                case KoronusActivityOwnedKind.Npc:
                    ownedNpcs++;
                    break;
                default:
                    ownedEntities++;
                    break;
            }
        }

        if (!hasDirector)
            return new KoronusActivityLongRunStatus(
                false,
                0,
                KoronusActivityDayFocus.Neutral,
                0,
                0,
                0,
                ownedEntities,
                ownedGrids,
                ownedNpcs,
                orphanCandidates,
                TimeSpan.Zero,
                0,
                0,
                0);

        var now = _timing.CurTime;
        var leased = component.Instances.Where(instance => instance.HasSystemLease).ToArray();
        var oldestLeaseAt = leased
            .Select(instance => instance.LeaseStartedAt ?? instance.CreatedAt)
            .DefaultIfEmpty(now)
            .Min();
        var history24 = component.History.Where(entry => now - entry.OccurredAt <= TimeSpan.FromHours(24)).ToArray();
        var history96 = component.History.Where(entry => now - entry.OccurredAt <= TimeSpan.FromHours(96)).ToArray();
        return new KoronusActivityLongRunStatus(
            true,
            component.DayIndex,
            component.DayFocus,
            component.Instances.Count(instance => instance.State == KoronusActivityState.Available),
            component.Instances.Count(instance => instance.State == KoronusActivityState.Engaged),
            leased.Length,
            ownedEntities,
            ownedGrids,
            ownedNpcs,
            orphanCandidates,
            leased.Length == 0 ? TimeSpan.Zero : now - oldestLeaseAt,
            history24.Length,
            history96.Length,
            history24.Select(entry => entry.Family).Distinct().Count());
    }

    /// <summary>
    /// Removes only explicit activity-owned entities whose instance is no longer active. This is
    /// the idempotent recovery path after an interrupted materialization or teardown; grids still
    /// evacuate attached players before deletion and no map is loaded merely to reconcile it.
    /// </summary>
    public int ReconcileOrphanedOwnedEntities()
    {
        var activeIds = TryGetActiveComponent(out var component)
            ? component.Instances.Select(instance => instance.Id).ToHashSet()
            : [];
        var orphanIds = new HashSet<long>();
        var owned = EntityQueryEnumerator<KoronusActivityOwnedComponent>();
        while (owned.MoveNext(out _, out var marker))
        {
            if (marker.InstanceId > 0 && !activeIds.Contains(marker.InstanceId))
                orphanIds.Add(marker.InstanceId);
        }

        var removed = 0;
        foreach (var instanceId in orphanIds)
            removed += CleanupOwnedEntities(instanceId);

        return removed;
    }

    /// <summary>
    /// Deterministic day-family selection. A saved round seed and day index always reproduce the
    /// same focus after a restart without introducing a scripted seven-day calendar.
    /// </summary>
    public static KoronusActivityDayFocus SelectDayFocus(int roundSeed, int dayIndex)
    {
        var focus = KoronusActivityDayFocus.Neutral;
        var count = (int) KoronusActivityDayFocus.AncientSignal + 1;
        for (var day = 0; day <= Math.Max(0, dayIndex); day++)
        {
            var next = (KoronusActivityDayFocus) (GetDeterministicValue(roundSeed, day, 0xD4A1u) % (uint) count);
            if (day > 0 && next == focus)
            {
                var shift = 1 + (int) (GetDeterministicValue(roundSeed, day, 0xA11Eu) % (uint) (count - 1));
                next = (KoronusActivityDayFocus) (((int) focus + shift) % count);
            }

            focus = next;
        }

        return focus;
    }

    private bool TryGetActiveComponent(out KoronusActivityDirectorComponent component)
    {
        if (_activeDirector is { } uid &&
            TryComp(uid, out KoronusActivityDirectorComponent? found) &&
            found.Active)
        {
            component = found;
            return true;
        }

        component = default!;
        return false;
    }

    private void RollDayProfile(KoronusActivityDirectorComponent component)
    {
        if (_timing.CurTime < component.NextDayProfileAt)
            return;

        do
        {
            component.DayIndex++;
            component.DayFocus = SelectDayFocus(component.RoundSeed, component.DayIndex);
            component.NextDayProfileAt += DayProfileDuration;
        } while (_timing.CurTime >= component.NextDayProfileAt);
    }

    private bool ExpireActivities(KoronusActivityDirectorComponent component)
    {
        var expired = component.Instances
            .Where(instance => _timing.CurTime >= instance.ExpiresAt)
            .ToArray();

        foreach (var instance in expired)
        {
            Archive(component, instance, KoronusActivityTerminalReason.Expired);
        }

        return expired.Length > 0;
    }

    private bool EnsureMinimumPopulation(KoronusActivityDirectorComponent component)
    {
        var systems = GetEligibleSystems(component);
        var templates = GetTemplates();
        var desired = KoronusActivityRuntimePolicy.GetInitialPopulation(systems.Count, templates.Count);
        var created = false;

        while (component.Instances.Count < desired && TryCreateActivity(component, systems, templates, out _))
        {
            created = true;
        }

        return created;
    }

    private bool TryCreateActivity(KoronusActivityDirectorComponent component, out KoronusActivityRuntimeInstance? instance)
    {
        return TryCreateActivity(component, GetEligibleSystems(component), GetTemplates(), out instance);
    }

    private bool TryCreateActivity(
        KoronusActivityDirectorComponent component,
        List<KoronusSystemPrototype> systems,
        List<KoronusActivityTemplatePrototype> templates,
        out KoronusActivityRuntimeInstance? instance)
    {
        instance = null;
        if (component.Sector == null || systems.Count == 0 || templates.Count == 0)
            return false;

        var routes = GetEligibleRoutes(component, systems);
        var candidates = new List<(KoronusActivityTemplatePrototype Template, int Weight)>();
        foreach (var template in templates)
        {
            // A set piece has a code-fixed destination. Exclude it before the deterministic
            // weighted pick when that system is disabled or outside the active sector; otherwise
            // the same NextInstanceId keeps selecting the unusable template and population can
            // remain permanently below its minimum.
            if (KoronusActivityRuntimePolicy.TryGetSetPieceSystem(template.Execution, out var fixedSystemId) &&
                systems.All(system => system.ID != fixedSystemId))
            {
                continue;
            }

            if (!CanUseTemplate(component, template, routes.Count > 0))
                continue;

            candidates.Add((template, GetTemplateWeight(component, template)));
        }

        var selected = PickWeightedTemplate(component.RoundSeed, component.NextInstanceId, candidates);
        if (selected == null)
            return false;

        string systemId;
        string? routeFrom = null;
        string? routeTo = null;
        if (KoronusActivityRuntimePolicy.TryGetSetPieceSystem(selected.Execution, out var setPieceSystemId))
        {
            var setPieceSystem = systems.FirstOrDefault(system => system.ID == setPieceSystemId);
            if (setPieceSystem == null)
                return false;

            systemId = setPieceSystem.ID;
        }
        else if (selected.Family == KoronusActivityFamily.Route)
        {
            if (routes.Count == 0)
                return false;

            var route = PickRoute(component, routes);
            routeFrom = route.From.Id;
            routeTo = route.To.Id;
            systemId = route.To.Id;
        }
        else
        {
            var system = PickSystem(component, systems);
            systemId = system.ID;
        }

        var id = component.NextInstanceId++;
        instance = new KoronusActivityRuntimeInstance
        {
            Id = id,
            TemplateId = selected.ID,
            SystemId = systemId,
            RouteFromSystem = routeFrom,
            RouteToSystem = routeTo,
            State = KoronusActivityState.Available,
            CreatedAt = _timing.CurTime,
            ExpiresAt = _timing.CurTime + GetLifetime(component.RoundSeed, id, selected),
            ThreatState = selected.Family == KoronusActivityFamily.Threat
                ? KoronusActivityThreatState.Intel
                : KoronusActivityThreatState.None,
        };
        component.Instances.Add(instance);
        return true;
    }

    private bool CanUseTemplate(
        KoronusActivityDirectorComponent component,
        KoronusActivityTemplatePrototype template,
        bool hasEligibleRoute)
    {
        if (template.Weight <= 0 ||
            template.Family == KoronusActivityFamily.Route && !hasEligibleRoute ||
            component.Instances.Any(instance => instance.TemplateId == template.ID))
        {
            return false;
        }

        if (template.Family == KoronusActivityFamily.Threat &&
            component.Instances.Any(instance =>
                _prototypes.TryIndex<KoronusActivityTemplatePrototype>(instance.TemplateId, out var activeTemplate) &&
                activeTemplate.Family == KoronusActivityFamily.Threat))
        {
            return false;
        }

        var latest = component.History.LastOrDefault(entry => entry.TemplateId == template.ID);
        return latest == null || _timing.CurTime >= latest.OccurredAt + TimeSpan.FromSeconds(Math.Max(0f, template.Cooldown));
    }

    private int GetTemplateWeight(KoronusActivityDirectorComponent component, KoronusActivityTemplatePrototype template)
    {
        var weight = Math.Max(1, template.Weight);
        if (template.PreferredDayFocus.Contains(component.DayFocus))
            weight *= 2;

        var recent = component.History.TakeLast(DiversityHistoryWindow);
        if (recent.Any(entry => entry.Family == template.Family))
            weight = Math.Max(1, weight / 2);
        if (recent.Count(entry => entry.Lane == template.Lane) >= 2)
            weight = Math.Max(1, weight / 2);

        var latest = component.History.LastOrDefault(entry => entry.TemplateId == template.ID);
        if (latest == null)
            return weight;

        return latest.Reason switch
        {
            KoronusActivityTerminalReason.Completed or
            KoronusActivityTerminalReason.Defeated or
            KoronusActivityTerminalReason.Contained => Math.Max(1, weight / 2),
            KoronusActivityTerminalReason.Expired => Math.Max(1, weight / 2),
            KoronusActivityTerminalReason.Abandoned or
            KoronusActivityTerminalReason.Evaded => 1,
            _ => weight,
        };
    }

    private List<KoronusSystemPrototype> GetEligibleSystems(KoronusActivityDirectorComponent component)
    {
        if (component.Sector is not { } sector)
            return [];

        return _prototypes.EnumeratePrototypes<KoronusSystemPrototype>()
            .Where(system =>
                system.Sector == sector &&
                KoronusActivityRuntimePolicy.IsSystemEligible(system.ID, system.Enabled, system.ActivityEligible))
            .OrderBy(system => system.ID, StringComparer.Ordinal)
            .ToList();
    }

    private List<KoronusRoutePrototype> GetEligibleRoutes(
        KoronusActivityDirectorComponent component,
        List<KoronusSystemPrototype> systems)
    {
        if (component.Sector is not { } sector)
            return [];

        var systemIds = systems.Select(system => system.ID).ToHashSet(StringComparer.Ordinal);
        return _prototypes.EnumeratePrototypes<KoronusRoutePrototype>()
            .Where(route =>
                route.Sector == sector &&
                route.Enabled &&
                systemIds.Contains(route.From.Id) &&
                systemIds.Contains(route.To.Id))
            .OrderBy(route => route.ID, StringComparer.Ordinal)
            .ToList();
    }

    private List<KoronusActivityTemplatePrototype> GetTemplates()
    {
        return _prototypes.EnumeratePrototypes<KoronusActivityTemplatePrototype>()
            .OrderBy(template => template.ID, StringComparer.Ordinal)
            .ToList();
    }

    private KoronusSystemPrototype PickSystem(
        KoronusActivityDirectorComponent component,
        List<KoronusSystemPrototype> systems)
    {
        var candidates = systems.Select(system => (System: system, Weight: GetSystemWeight(component, system.ID))).ToList();
        var totalWeight = candidates.Sum(candidate => candidate.Weight);
        var choice = (int) (GetDeterministicValue(component.RoundSeed, component.NextInstanceId, 0xF541u) % (uint) totalWeight);
        foreach (var candidate in candidates)
        {
            if (choice < candidate.Weight)
                return candidate.System;

            choice -= candidate.Weight;
        }

        return candidates[^1].System;
    }

    private KoronusRoutePrototype PickRoute(
        KoronusActivityDirectorComponent component,
        List<KoronusRoutePrototype> routes)
    {
        var recent = component.History.TakeLast(SystemHistoryWindow).ToArray();
        var candidates = routes.Select(route =>
        {
            var repetitions = recent.Count(entry => entry.RouteFromSystem == route.From.Id && entry.RouteToSystem == route.To.Id);
            return (Route: route, Weight: Math.Max(1, 8 / (1 + repetitions * 3)));
        }).ToList();
        var totalWeight = candidates.Sum(candidate => candidate.Weight);
        var choice = (int) (GetDeterministicValue(component.RoundSeed, component.NextInstanceId, 0x5219u) % (uint) totalWeight);
        foreach (var candidate in candidates)
        {
            if (choice < candidate.Weight)
                return candidate.Route;

            choice -= candidate.Weight;
        }

        return candidates[^1].Route;
    }

    private static int GetSystemWeight(KoronusActivityDirectorComponent component, string systemId)
    {
        var recentUses = component.History.TakeLast(SystemHistoryWindow).Count(entry => entry.SystemId == systemId);
        var activeUses = component.Instances.Count(instance => instance.SystemId == systemId);
        return Math.Max(1, 16 / (1 + recentUses * 3 + activeUses * 4));
    }

    private static KoronusActivityTemplatePrototype? PickWeightedTemplate(
        int roundSeed,
        long instanceId,
        List<(KoronusActivityTemplatePrototype Template, int Weight)> candidates)
    {
        var totalWeight = candidates.Sum(candidate => candidate.Weight);
        if (totalWeight <= 0)
            return null;

        var choice = (int) (GetDeterministicValue(roundSeed, instanceId, 0x0F11u) % (uint) totalWeight);
        foreach (var candidate in candidates)
        {
            if (choice < candidate.Weight)
                return candidate.Template;

            choice -= candidate.Weight;
        }

        return candidates[^1].Template;
    }

    private static TimeSpan GetLifetime(int roundSeed, long instanceId, KoronusActivityTemplatePrototype template)
    {
        var minimum = Math.Max((float) MinimumLifetime.TotalSeconds, MathF.Min(template.MinimumLifetime, template.MaximumLifetime));
        var maximum = MathF.Max(minimum, MathF.Max(template.MinimumLifetime, template.MaximumLifetime));
        var spread = (uint) Math.Max(0, (int) MathF.Floor(maximum - minimum));
        var seconds = minimum + GetDeterministicValue(roundSeed, instanceId, 0xBB13u) % (spread + 1u);
        return TimeSpan.FromSeconds(seconds);
    }

    private void ScheduleNextSpawn(KoronusActivityDirectorComponent component)
    {
        var delaySeconds = 600u + GetDeterministicValue(component.RoundSeed, component.NextInstanceId, 0x771Cu) % 901u;
        component.NextSpawnAt = _timing.CurTime + TimeSpan.FromSeconds(delaySeconds);
    }

    private bool Archive(
        KoronusActivityDirectorComponent component,
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTerminalReason reason,
        EntityUid? preservedObjective = null)
    {
        if (!KoronusActivityLifecycle.CanTransition(instance.State, KoronusActivityState.CleanupPending))
            return false;

        instance.State = KoronusActivityState.CleanupPending;
        if (!KoronusActivityLifecycle.CanTransition(instance.State, KoronusActivityState.Archived))
            return false;

        instance.State = KoronusActivityState.Archived;
        CleanupOwnedEntities(instance, preservedObjective);
        component.Instances.Remove(instance);
        var family = KoronusActivityFamily.State;
        var lane = KoronusActivityMarkerLane.Ambient;
        if (_prototypes.TryIndex<KoronusActivityTemplatePrototype>(instance.TemplateId, out var template))
        {
            family = template.Family;
            lane = template.Lane;
        }

        component.History.Add(new KoronusActivityHistoryEntry
        {
            InstanceId = instance.Id,
            TemplateId = instance.TemplateId,
            SystemId = instance.SystemId,
            RouteFromSystem = instance.RouteFromSystem,
            RouteToSystem = instance.RouteToSystem,
            Family = family,
            Lane = lane,
            Reason = reason,
            OccurredAt = _timing.CurTime,
        });

        if (component.History.Count > MaximumHistoryEntries)
            component.History.RemoveRange(0, component.History.Count - MaximumHistoryEntries);

        return true;
    }

    /// <summary>
    /// Deletes only roots that explicitly belong to this activity. Players attached to a temporary
    /// owned grid are first moved onto the already cleared system map, so deleting a set piece
    /// cannot delete or orphan a player body. It never addresses a map, shuttle, surface, or a
    /// nearby unowned entity.
    /// </summary>
    private void CleanupOwnedEntities(KoronusActivityRuntimeInstance instance, EntityUid? preservedObjective = null)
    {
        CleanupOwnedEntities(instance.Id, preservedObjective);

        if (instance.HasSystemLease)
        {
            _residency.EndActivityLease(instance.SystemId, instance.Id);
            instance.HasSystemLease = false;
            instance.LeaseStartedAt = null;
        }

        instance.Objective = null;
    }

    private int CleanupOwnedEntities(long instanceId, EntityUid? preservedObjective = null)
    {
        var toDelete = new List<(EntityUid Uid, KoronusActivityOwnedKind Kind)>();
        var owned = EntityQueryEnumerator<KoronusActivityOwnedComponent>();
        while (owned.MoveNext(out var uid, out var marker))
        {
            if (marker.InstanceId == instanceId && uid != preservedObjective)
                toDelete.Add((uid, marker.Kind));
        }

        foreach (var (uid, kind) in toDelete)
        {
            if (kind == KoronusActivityOwnedKind.Grid)
            {
                // The sole dockable profile is a static wreck. Detaching only its own ports before
                // removal leaves the other, player-owned grid intact and lets its FTL workflow
                // proceed independently.
                _docking.UndockDocks(uid);
                EvacuateAttachedPlayers(uid);
            }
        }

        foreach (var (uid, _) in toDelete)
            QueueDel(uid);
        return toDelete.Count;
    }

    private void EvacuateAttachedPlayers(EntityUid grid)
    {
        if (!TryComp<TransformComponent>(grid, out var gridTransform))
            return;

        var gridPosition = _transform.ToMapCoordinates(gridTransform.Coordinates);
        // Activity grids reserve an empty 36 m band on creation. Thirty metres puts a body
        // outside even the cutter hull while retaining a known-safe system map rather than a map
        // or grid selected by player input.
        var fallback = new MapCoordinates(gridPosition.Position + Vector2.UnitX * 30f, gridPosition.MapId);
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player ||
                !TryComp<TransformComponent>(player, out var playerTransform) ||
                playerTransform.GridUid != grid)
            {
                continue;
            }

            _transform.SetMapCoordinates(player, fallback);
        }
    }

    private void RebuildPresentation(KoronusActivityDirectorComponent component)
    {
        component.Markers.Clear();
        foreach (var instance in component.Instances)
        {
            if (!_prototypes.TryIndex<KoronusActivityTemplatePrototype>(instance.TemplateId, out var template))
                continue;

            component.Markers.Add(new KoronusActivityMarkerState(
                instance.Id,
                instance.SystemId,
                template.Lane,
                template.Risk,
                template.Reliability,
                template.TitleLocId,
                instance.ExpiresAt));
        }
    }

    private static KoronusActivityDossierState CreateDossier(
        KoronusActivityRuntimeInstance instance,
        KoronusActivityTemplatePrototype template,
        KoronusActivityTerminalReason? terminalReason)
    {
        return new KoronusActivityDossierState(
            instance.Id,
            instance.SystemId,
            instance.RouteFromSystem,
            instance.RouteToSystem,
            template.Family,
            template.Lane,
            instance.State,
            template.Risk,
            template.Reliability,
            template.ThreatFamily,
            template.ThreatLevel,
            instance.ThreatState,
            terminalReason,
            template.TitleLocId,
            template.SummaryLocId,
            instance.ExpiresAt);
    }

    private static KoronusActivityThreatState GetTerminalThreatState(
        KoronusActivityTemplatePrototype template,
        KoronusActivityTerminalReason reason)
    {
        if (template.Family != KoronusActivityFamily.Threat)
            return KoronusActivityThreatState.None;

        return reason switch
        {
            KoronusActivityTerminalReason.Defeated => KoronusActivityThreatState.Defeated,
            KoronusActivityTerminalReason.Contained => KoronusActivityThreatState.Contained,
            KoronusActivityTerminalReason.Evaded => KoronusActivityThreatState.Evaded,
            KoronusActivityTerminalReason.SafetyViolation => KoronusActivityThreatState.SafetyViolation,
            _ => KoronusActivityThreatState.Intel,
        };
    }

    private static uint GetDeterministicValue(int roundSeed, long sequence, uint salt)
    {
        unchecked
        {
            uint value = (uint) roundSeed;
            value ^= (uint) sequence;
            value ^= (uint) (sequence >> 32);
            value ^= salt + 0x9E3779B9u + (value << 6) + (value >> 2);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ value >> 16;
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        if (_activeDirector is { } uid && TryComp(uid, out KoronusActivityDirectorComponent? component))
        {
            foreach (var instance in component.Instances.ToArray())
            {
                Archive(component, instance, KoronusActivityTerminalReason.RoundEnded);
            }

            component.Active = false;
            component.Markers.Clear();
            component.Instances.Clear();
            component.History.Clear();
            component.Sector = null;
        }

        _activeDirector = null;
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
    }
}

public readonly record struct KoronusActivityDirectorStatus(
    int RoundSeed,
    int DayIndex,
    KoronusActivityDayFocus DayFocus,
    TimeSpan NextSpawnAt,
    int ActiveCount,
    int HistoryCount);
