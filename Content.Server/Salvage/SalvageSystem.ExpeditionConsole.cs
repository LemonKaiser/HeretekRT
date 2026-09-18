using Content.Shared.Popups;
using Content.Shared.Station.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Content.Server.Salvage.Expeditions; // Frontier
using Content.Shared._NF.CCVar; // Frontier
using Content.Shared.Mind.Components; // Frontier
using Content.Shared.Mobs.Components; // Frontier
using Content.Shared.NPC.Components; // Frontier
using Content.Shared.IdentityManagement; // Frontier
using Content.Shared.NPC; // Frontier
using Content.Server._NF.Salvage; // Frontier
using Content.Server.Shuttles.Components;
using Content.Shared._WH40K.OperationalDomain;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    [ValidatePrototypeId<EntityPrototype>]
    public const string CoordinatesDisk = "CoordinatesDisk";
    private const float ShuttleFTLRange = 256f;
    private const float ShuttleFTLMassThreshold = 100f;

    [Dependency] private SharedPopupSystem _popupSystem = default!;

    private void OnSalvageClaimMessage(EntityUid uid, SalvageExpeditionConsoleComponent component, ClaimSalvageMessage args)
    {
        if (!TryGetExpeditionOwner(uid, out var owner, out var data, out var grid) || data.Claimed || data.Cooldown)
            return;

        var activeExpeditionCount = 0;
        var expeditionQuery = AllEntityQuery<SalvageExpeditionDataComponent, MetaDataComponent>();
        while (expeditionQuery.MoveNext(out var expeditionUid, out _, out _))
            if (TryComp<SalvageExpeditionDataComponent>(expeditionUid, out var expeditionData) && expeditionData.Claimed)
                activeExpeditionCount++;

        if (activeExpeditionCount >= _cfgManager.GetCVar(NFCCVars.SalvageExpeditionMaxActive))
        {
            PlayDenySound(uid, component);
            _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-too-many"), uid, PopupType.MediumCaution);
            UpdateConsoles(owner, data);
            return;
        }
        // End Frontier

        if (!data.Missions.TryGetValue(args.Index, out var missionparams))
            return;

        if (!component.Debug) // Skip the test
        {
            if (!TryComp<MapGridComponent>(grid, out var gridComp) || !HasComp<ShuttleComponent>(grid))
            {
                PlayDenySound(uid, component);
                _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), uid, PopupType.MediumCaution);
                UpdateConsoles(owner, data);
                return;
            }

            if (IsFtlActive(grid))
            {
                PlayDenySound(uid, component);
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-recharge"), uid, PopupType.MediumCaution);
                UpdateConsoles(owner, data);
                return;
            }

            var xform = Transform(grid);
            var bounds = xform.WorldMatrix.TransformBox(gridComp.LocalAABB).Enlarged(ShuttleFTLRange);
            var bodyQuery = GetEntityQuery<PhysicsComponent>();
            // Keep track of docked grids to exclude them from the proximity check
            var dockedGrids = new HashSet<EntityUid>();

            // Find all docked grids by looking for DockingComponents on the shuttle
            var dockQuery = EntityQueryEnumerator<DockingComponent, TransformComponent>();
            while (dockQuery.MoveNext(out var dockUid, out var dock, out var dockXform))
            {
                // Only consider docks on our grid
                if (dockXform.GridUid != grid || !dock.Docked || dock.DockedWith == null)
                    continue;

                // If we have a docked entity, get its grid
                if (TryComp<TransformComponent>(dock.DockedWith.Value, out var dockedXform) && dockedXform.GridUid != null)
                {
                    dockedGrids.Add(dockedXform.GridUid.Value);

                    // Check if we're docked to another grid
                    var parentGridUid = dockedXform.GridUid.Value;

                    // Find all other grids docked to this parent grid
                    // These should also be excluded from the proximity check so we can
                    // still FTL even when other ships are docked to the same station/grid
                    var parentDockQuery = EntityQueryEnumerator<DockingComponent, TransformComponent>();
                    while (parentDockQuery.MoveNext(out var parentDockUid, out var parentDock, out var parentDockXform))
                    {
                        // Only consider docks on the parent grid
                        if (parentDockXform.GridUid != parentGridUid || !parentDock.Docked || parentDock.DockedWith == null)
                            continue;

                        // If we have a docked entity and it's not our grid, add its grid to the exclusion list
                        if (TryComp<TransformComponent>(parentDock.DockedWith.Value, out var siblingDockedXform) &&
                            siblingDockedXform.GridUid != null &&
                            siblingDockedXform.GridUid != grid)
                        {
                            dockedGrids.Add(siblingDockedXform.GridUid.Value);
                        }
                    }
                }
            }

            foreach (var other in _mapManager.FindGridsIntersecting(xform.MapID, bounds))
            {
                if (other.Owner == grid ||
                    dockedGrids.Contains(other.Owner) || // Skip grids that are docked to us or to the same parent grid
                    !bodyQuery.TryGetComponent(other.Owner, out var body) ||
                    body.Mass < ShuttleFTLMassThreshold)
                {
                    continue;
                }

                PlayDenySound(uid, component);
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-proximity"), uid, PopupType.Medium);
                UpdateConsoles(owner, data);
                return;
            }
        }
        // End Frontier

        // Frontier  change - disable coordinate disks for expedition missions
        //var cdUid = Spawn(CoordinatesDisk, Transform(uid).Coordinates);
        SpawnMission(missionparams, owner, grid, null);

        data.ActiveMission = args.Index;
        var mission = GetMission(missionparams.MissionType, missionparams.Difficulty, missionparams.Seed);
        data.NextOffer = _timing.CurTime + mission.Duration + TimeSpan.FromSeconds(1);

        // Frontier  change - disable coordinate disks for expedition missions
        //_labelSystem.Label(cdUid, GetFTLName(_prototypeManager.Index<LocalizedDatasetPrototype>("NamesBorer"), missionparams.Seed));
        //_audio.PlayPvs(component.PrintSound, uid);

        UpdateConsoles(owner, data);
    }

    // Frontier: early expedition end
    private void OnSalvageFinishMessage(EntityUid entity, SalvageExpeditionConsoleComponent component, FinishSalvageMessage e)
    {
        if (!TryGetExpeditionOwner(entity, out var owner, out var data, out _) || !data.CanFinish)
            return;

        // Based on SalvageSystem.Runner:OnConsoleFTLAttempt
        if (!TryComp(entity, out TransformComponent? xform)) // Get the console's grid (if you move it, rip you)
        {
            PlayDenySound(entity, component);
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), entity, PopupType.MediumCaution);
            UpdateConsoles(owner, data);
            return;
        }

        // Frontier: check if any player characters or friendly ghost roles are outside
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mindContainer, out var _, out var mobXform))
        {
            if (mobXform.MapUid != xform.MapUid)
                continue;

            // Not player controlled (ghosted)
            if (!mindContainer.HasMind)
                continue;

            // NPC, definitely not a person
            if (HasComp<ActiveNPCComponent>(uid) || HasComp<NFSalvageMobRestrictionsComponent>(uid))
                continue;

            // Hostile ghost role, continue
            if (TryComp(uid, out NpcFactionMemberComponent? npcFaction))
            {
                var hostileFactions = npcFaction.HostileFactions;
                if (hostileFactions.Contains("NanoTrasen")) // Nasty - what if we need pirate expeditions?
                    continue;
            }

            // Okay they're on salvage, so are they on the shuttle.
            if (mobXform.GridUid != xform.GridUid)
            {
                PlayDenySound(entity, component);
                _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-not-everyone-aboard", ("target", Identity.Entity(uid, EntityManager))), entity, PopupType.MediumCaution);
                UpdateConsoles(owner, data);
                return;
            }
        }
        // End SalvageSystem.Runner:OnConsoleFTLAttempt

        var map = Transform(entity).MapUid;

        if (!TryComp<SalvageExpeditionComponent>(map, out var expedition) || expedition.Station != owner)
            return;

        data.CanFinish = false;
        UpdateConsoles(owner, data);

        const int departTime = 20;
        var newEndTime = _timing.CurTime + TimeSpan.FromSeconds(departTime);

        if (expedition.EndTime <= newEndTime)
            return;

        expedition.EndTime = newEndTime;
        expedition.Stage = ExpeditionStage.FinalCountdown;

        Announce(map.Value, Loc.GetString("salvage-expedition-announcement-early-finish", ("departTime", departTime)));
    }
    // End Frontier: early expedition end

    private void OnSalvageConsoleInit(Entity<SalvageExpeditionConsoleComponent> console, ref ComponentInit args)
    {
        UpdateConsole(console);
    }

    private void OnSalvageConsoleParent(Entity<SalvageExpeditionConsoleComponent> console, ref EntParentChangedMessage args)
    {
        UpdateConsole(console);
    }

    private void UpdateConsoles(EntityUid stationUid, SalvageExpeditionDataComponent component)
    {
        var state = GetState(component);

        if (!TryGetExpeditionGrid(stationUid, out var grid) || IsFtlActive(grid))
            state.Cooldown = true;

        var query = AllEntityQuery<SalvageExpeditionConsoleComponent, UserInterfaceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var uiComp, out _))
        {
            if (!TryGetExpeditionOwner(uid, out var owner, out _, out _) || owner != stationUid)
                continue;

            _ui.SetUiState((uid, uiComp), SalvageConsoleUiKey.Expedition, state);
        }
    }

    private void UpdateConsole(Entity<SalvageExpeditionConsoleComponent> component)
    {
        SalvageExpeditionConsoleState state;

        if (TryGetExpeditionOwner(component.Owner, out _, out var dataComponent, out var grid))
        {
            state = GetState(dataComponent);
            if (IsFtlActive(grid))
                state.Cooldown = true;
        }
        else
        {
            state = new SalvageExpeditionConsoleState(TimeSpan.Zero, false, true, false, 0, new List<SalvageMissionParams>()); // Frontier: add false as 4th param
        }

        _ui.SetUiState(component.Owner, SalvageConsoleUiKey.Expedition, state);
    }

    /// <summary>
    /// Resolves the single owner used by every expedition operation.
    /// An independent vessel grid owns its expedition data directly. A purchased vessel with its
    /// own station domain keeps the data on that station; other stations require explicit data.
    /// </summary>
    internal bool TryGetExpeditionOwner(
        EntityUid entity,
        out EntityUid owner,
        out SalvageExpeditionDataComponent data,
        out EntityUid grid)
    {
        owner = EntityUid.Invalid;
        data = default!;
        grid = EntityUid.Invalid;

        if (!_operationalDomains.TryResolveOperationalDomain(entity, out var domain))
            return false;

        owner = domain.Owner;
        grid = domain.PrimaryGrid;
        if (TryComp<SalvageExpeditionDataComponent>(owner, out var existing) && existing != null)
        {
            data = existing;
            return true;
        }

        if (domain.Kind != OperationalDomainKind.Vessel &&
            (domain.Kind != OperationalDomainKind.Station ||
             !_operationalDomains.IsVesselGrid(domain.PrimaryGrid)))
            return false;

        data = EnsureComp<SalvageExpeditionDataComponent>(owner);
        return true;
    }

    private bool TryGetExpeditionGrid(EntityUid owner, out EntityUid grid)
    {
        grid = default;

        if (HasComp<MapGridComponent>(owner))
        {
            grid = owner;
            return true;
        }

        if (!TryComp<StationDataComponent>(owner, out var stationData) ||
            _station.GetLargestGrid((owner, stationData)) is not { Valid: true } largestGrid)
        {
            return false;
        }

        grid = largestGrid;
        return true;
    }

    private bool IsFtlActive(EntityUid grid)
    {
        return TryComp<FTLComponent>(grid, out var ftl) &&
               ftl.State is FTLState.Starting or FTLState.Travelling or FTLState.Arriving or FTLState.Cooldown;
    }

    private void PlayDenySound(EntityUid uid, SalvageExpeditionConsoleComponent component)
    {
        _audio.PlayPvs(_audio.GetSound(component.ErrorSound), uid);
    }
}
