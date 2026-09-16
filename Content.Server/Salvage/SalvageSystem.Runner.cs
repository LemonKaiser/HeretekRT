using System.Numerics;
using Content.Server.Salvage.Expeditions;
using Content.Server.Salvage.Expeditions.Structure;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server._WH40K.Progression;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Localizations;
using Content.Shared.Station.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using Content.Shared.Coordinates;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    /*
     * Handles actively running a salvage expedition.
     */

    [Dependency] private MobStateSystem _mobState = default!;
    private void InitializeRunner()
    {
        SubscribeLocalEvent<ShuttleComponent, FTLRequestEvent>(OnFTLRequest);
        SubscribeLocalEvent<FTLStartedEvent>(OnFTLStarted);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFTLCompleted);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnConsoleFTLAttempt);
    }

    private void OnConsoleFTLAttempt(ref ConsoleFTLAttemptEvent ev)
    {
        if (!TryComp(ev.Uid, out TransformComponent? xform) ||
            !TryComp<SalvageExpeditionComponent>(xform.MapUid, out var salvage))
        {
            return;
        }

        // TODO: This is terrible but need bluespace harnesses or something.
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var _, out var mobState, out var mobXform))
        {
            if (mobXform.MapUid != xform.MapUid)
                continue;

            // Don't count unidentified humans (loot) or anyone you murdered so you can still maroon them once dead.
            if (_mobState.IsDead(uid, mobState))
                continue;

            // Okay they're on salvage, so are they on the shuttle.
            if (mobXform.GridUid != ev.Uid)
            {
                ev.Cancelled = true;
                ev.Reason = Loc.GetString("salvage-expedition-not-all-present");
                return;
            }
        }
    }

    /// <summary>
    /// Announces status updates to salvage crewmembers on the state of the expedition.
    /// </summary>
    private void Announce(EntityUid mapUid, string text)
    {
        var mapId = Comp<MapComponent>(mapUid).MapId;

        // I love TComms and chat!!!
        _chat.ChatMessageToManyFiltered(
            Filter.BroadcastMap(mapId),
            ChatChannel.Radio,
            text,
            text,
            _mapManager.GetMapEntityId(mapId),
            false,
            true,
            null);
    }

    private void OnFTLRequest(Entity<ShuttleComponent> shuttle, ref FTLRequestEvent ev)
    {
        if (!TryComp<SalvageExpeditionComponent>(ev.MapUid, out var expedition) ||
            !TryGetExpeditionGrid(expedition.Station, out var ownerGrid) ||
            ownerGrid != shuttle.Owner ||
            !TryComp<FTLDestinationComponent>(ev.MapUid, out var dest))
        {
            return;
        }

        // Only one shuttle can occupy an expedition.
        dest.Enabled = false;
        _shuttleConsoles.RefreshShuttleConsoles();
    }

    private void OnFTLCompleted(ref FTLCompletedEvent args)
    {
        if (!TryComp<SalvageExpeditionComponent>(args.MapUid, out var component))
            return;

        if (!TryGetExpeditionGrid(component.Station, out var ownerGrid) || ownerGrid != args.Entity)
            return;

        // Frontier
        if (TryComp<SalvageExpeditionDataComponent>(component.Station, out var data))
        {
            data.CanFinish = true;
            UpdateConsoles(component.Station, data);
        }
        // Frontier

        // Someone FTLd there so start announcement
        if (component.Stage != ExpeditionStage.Added)
            return;

        Announce(args.MapUid, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", (component.EndTime - _timing.CurTime).Minutes)));

        var directionLocalization = ContentLocalizationManager.FormatDirection(component.DungeonLocation.GetDir()).ToLower();

        if (component.DungeonLocation != Vector2.Zero)
            Announce(args.MapUid, Loc.GetString("salvage-expedition-announcement-dungeon", ("direction", directionLocalization)));

        component.Stage = ExpeditionStage.Running;
        Dirty(args.MapUid, component);
    }

    private void OnFTLStarted(ref FTLStartedEvent ev)
    {
        // Started a mining mission so work out exempt entities
        if (TryComp<SalvageMiningExpeditionComponent>(
                _mapManager.GetMapEntityId(ev.TargetCoordinates.ToMap(EntityManager, _transform).MapId),
                out var mining))
        {
            var ents = new List<EntityUid>();
            var xformQuery = GetEntityQuery<TransformComponent>();
            MiningTax(ents, ev.Entity, mining, xformQuery);
            mining.ExemptEntities = ents;
        }

        if (!TryComp<SalvageExpeditionComponent>(ev.FromMapUid, out var expedition) ||
            !TryGetExpeditionGrid(expedition.Station, out var ownerGrid) ||
            ownerGrid != ev.Entity ||
            !TryComp<SalvageExpeditionDataComponent>(expedition.Station, out var station))
        {
            return;
        }

        station.CanFinish = false; // Frontier

        // Check if any shuttles remain.
        var query = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();

        while (query.MoveNext(out _, out var xform))
        {
            if (xform.MapUid == ev.FromMapUid)
                return;
        }

        // Last shuttle has left so finish the mission.
        QueueDel(ev.FromMapUid.Value);
    }

    // Runs the expedition
    private void UpdateRunner()
    {
        // Generic missions
        var query = EntityQueryEnumerator<SalvageExpeditionComponent>();

        // Run the basic mission timers (e.g. announcements, auto-FTL, completion, etc)
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!TryGetExpeditionGrid(comp.Station, out _))
            {
                QueueDel(uid);
                continue;
            }

            var remaining = comp.EndTime - _timing.CurTime;
            var audioLength = _audio.GetAudioLength(comp.SelectedSong);

            if (comp.Stage < ExpeditionStage.FinalCountdown && remaining < TimeSpan.FromSeconds(45))
            {
                comp.Stage = ExpeditionStage.FinalCountdown;
                Dirty(uid, comp);
                Announce(uid, Loc.GetString("salvage-expedition-announcement-countdown-seconds", ("duration", TimeSpan.FromSeconds(45).Seconds)));
            }
            else if (comp.Stage < ExpeditionStage.MusicCountdown && comp.Stream == null && remaining < audioLength) // Frontier
            {
                var audio = _audio.PlayPvs(comp.Sound, uid);
                comp.Stream = audio?.Entity;
                _audio.SetMapAudio(audio);
                comp.Stage = ExpeditionStage.MusicCountdown;
                Dirty(uid, comp);
                Announce(uid, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", audioLength.Minutes)));
            }
            else if (comp.Stage < ExpeditionStage.Countdown && remaining < TimeSpan.FromMinutes(5))
            {
                comp.Stage = ExpeditionStage.Countdown;
                Dirty(uid, comp);
                Announce(uid, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", TimeSpan.FromMinutes(5).Minutes)));
            }
            // Return the expedition owner's ship. The owner can be either a legacy station or
            // an independent vessel grid, but both are resolved through the same path.
            else if (remaining < TimeSpan.FromSeconds(_shuttle.DefaultStartupTime) + TimeSpan.FromSeconds(0.5))
            {
                var ftlTime = (float) remaining.TotalSeconds;
                if (remaining < TimeSpan.FromSeconds(_shuttle.DefaultStartupTime))
                    ftlTime = MathF.Max(0f, (float) remaining.TotalSeconds - 0.5f);

                ftlTime = MathF.Min(ftlTime, _shuttle.DefaultStartupTime);

                if (!TryGetExpeditionGrid(comp.Station, out var shuttleUid) ||
                    !TryComp<ShuttleComponent>(shuttleUid, out var shuttle) ||
                    Transform(shuttleUid).MapUid != uid ||
                    IsFtlActive(shuttleUid))
                {
                    continue;
                }

                if (!TryPrepareExpeditionReturn(comp, out var mapUid, out var mapId, out var reservedSystem))
                    continue;

                const int numRetries = 20;
                const float minDistance = 200f;
                const float minRange = 750f;
                const float maxRange = 3500f;

                List<Vector2> gridCoords = new();
                var gridQuery = EntityManager.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
                while (gridQuery.MoveNext(out var _, out _, out var xform))
                {
                    if (xform.MapID == mapId)
                        gridCoords.Add(_transform.GetWorldPosition(xform));
                }

                Vector2 dropLocation = _random.NextVector2(minRange, maxRange);
                for (var i = 0; i < numRetries; i++)
                {
                    var positionIsValid = true;
                    foreach (var station in gridCoords)
                    {
                        if (Vector2.Distance(station, dropLocation) < minDistance)
                        {
                            positionIsValid = false;
                            break;
                        }
                    }

                    if (positionIsValid)
                        break;

                    dropLocation = _random.NextVector2(minRange, maxRange);
                }

                _shuttle.FTLToCoordinates(shuttleUid, shuttle, new EntityCoordinates(mapUid, dropLocation), 0f, ftlTime, 50f);

                if (reservedSystem != null)
                {
                    if (TryComp<FTLComponent>(shuttleUid, out var ftl) && ftl.State == FTLState.Starting)
                        _shuttleConsoles.TrackExternalKoronusJumpReservation(shuttleUid, reservedSystem);
                    else
                        _koronusResidency.EndIncomingSectorJump(reservedSystem);
                }
            }

            if (remaining < TimeSpan.Zero)
            {
                // Never delete the expedition map while the owner's ship is still on it. If the
                // return destination is temporarily unavailable, keep retrying instead of deleting
                // the ship together with the expired map.
                if (TryGetExpeditionGrid(comp.Station, out var ownerGrid) &&
                    Transform(ownerGrid).MapUid == uid)
                {
                    continue;
                }

                QueueDel(uid);
            }
        }

        // Mining missions: NOOP since it's handled after ftling

        // Structure missions
        var structureQuery = EntityQueryEnumerator<SalvageStructureExpeditionComponent, SalvageExpeditionComponent>();

        while (structureQuery.MoveNext(out var uid, out var structure, out var comp))
        {
            if (comp.Completed)
                continue;

            var structureAnnounce = false;

            for (var i = 0; i < structure.Structures.Count; i++)
            {
                var objective = structure.Structures[i];

                if (Deleted(objective))
                {
                    structure.Structures.RemoveSwap(i);
                    structureAnnounce = true;
                }
            }

            if (structureAnnounce)
            {
                Announce(uid, Loc.GetString("salvage-expedition-structure-remaining", ("count", structure.Structures.Count)));
            }

            if (structure.Structures.Count == 0)
            {
                comp.Completed = true;
                Announce(uid, Loc.GetString("salvage-expedition-completed"));
                RaiseLocalEvent(new Wh40kSalvageExpeditionCompletedEvent(
                    Transform(uid).MapID,
                    comp.Difficulty,
                    comp.MissionParams.Seed));
            }
        }

        // Elimination missions
        var eliminationQuery = EntityQueryEnumerator<SalvageEliminationExpeditionComponent, SalvageExpeditionComponent>();
        while (eliminationQuery.MoveNext(out var uid, out var elimination, out var comp))
        {
            if (comp.Completed)
                continue;

            var announce = false;

            for (var i = 0; i < elimination.Megafauna.Count; i++)
            {
                var mob = elimination.Megafauna[i];

                if (Deleted(mob) || _mobState.IsDead(mob))
                {
                    elimination.Megafauna.RemoveSwap(i);
                    announce = true;
                }
            }

            if (announce)
            {
                Announce(uid, Loc.GetString("salvage-expedition-megafauna-remaining", ("count", elimination.Megafauna.Count)));
            }

            if (elimination.Megafauna.Count == 0)
            {
                comp.Completed = true;
                Announce(uid, Loc.GetString("salvage-expedition-completed"));
                RaiseLocalEvent(new Wh40kSalvageExpeditionCompletedEvent(
                    Transform(uid).MapID,
                    comp.Difficulty,
                    comp.MissionParams.Seed));
            }
        }
    }

    private bool TryPrepareExpeditionReturn(
        SalvageExpeditionComponent expedition,
        out EntityUid mapUid,
        out MapId mapId,
        out string? reservedSystem)
    {
        mapUid = EntityUid.Invalid;
        mapId = MapId.Nullspace;
        reservedSystem = null;

        if (!string.IsNullOrEmpty(expedition.ReturnSystemId))
        {
            var systemId = expedition.ReturnSystemId;
            if (!_koronusResidency.BeginIncomingSectorJump(systemId))
                return false;

            if (!_koronusSector.TryGetSystemMap(systemId, out mapId) ||
                !_mapSystem.TryGetMap(mapId, out var foundMap))
            {
                _koronusResidency.EndIncomingSectorJump(systemId);
                return false;
            }

            mapUid = foundMap.Value;
            reservedSystem = systemId;
            return true;
        }

        if (expedition.ReturnMap is not { Valid: true } returnMap ||
            !TryComp<MapComponent>(returnMap, out var map))
        {
            return false;
        }

        mapUid = returnMap;
        mapId = map.MapId;
        return true;
    }
}
