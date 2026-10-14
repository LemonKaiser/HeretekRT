using System.Linq;
using System.Numerics;
using Content.Server._WH40K.Activities;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Components;
using Content.Server.Emp;
using Content.Server.Power.Components;
using Content.Shared.StatusEffect;
using Content.Shared.Emp;
using Content.Shared.Stunnable;
using Content.Shared._WH40K.SectorMap;
using Content.Shared._WH40K.SectorMap.Events;
using Content.Shared.DoAfter;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Tiles;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared._WH40K.Activities;

namespace Content.Server._WH40K.SectorMap.Systems;

public sealed class KoronusWarpEncounterSystem : EntitySystem
{
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private ActivityContentAudit _audit = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WH40KPlayerCultureManager _culture = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private EmpSystem _emp = default!;
    [Dependency] private KoronusActivitySalvageSystem _salvage = default!;

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusWarpRiftComponent, InteractHandEvent>(OnInteract);
        SubscribeLocalEvent<KoronusWarpRiftComponent, KoronusWarpSealDoAfterEvent>(OnSeal);
        SubscribeLocalEvent<KoronusWarpEncounterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<KoronusWarpEncounterComponent, FTLCompletedEvent>(OnArrived);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
    }

    public void BeginJump(EntityUid shuttle, string from, string to)
    {
        if (!_sector.HasWarpRoute(from, to) ||
            !HasComp<FTLComponent>(shuttle))
            return;
        if (to == "Footfall")
            RemComp<KoronusWarpEncounterComponent>(shuttle);
        else if (TryComp<KoronusWarpEncounterComponent>(shuttle, out var active))
        {
            active.Risk = _sector.GetWarpRisk(from, to);
            return;
        }
        var encounter = EnsureComp<KoronusWarpEncounterComponent>(shuttle);
        encounter.Risk = _sector.GetWarpRisk(from, to);
        encounter.PlannedIncident = KoronusWarpRoutePolicy.SelectIncident(encounter.Risk,
            _random.NextFloat(), _random.NextFloat(), to);
    }

    public (KoronusWarpRisk Risk, KoronusWarpIncident Incident) GetStatus(EntityUid shuttle)
    {
        return TryComp<KoronusWarpEncounterComponent>(shuttle, out var encounter)
            ? (encounter.Risk, encounter.Resolved
                ? KoronusWarpIncident.None : encounter.Incident)
            : (KoronusWarpRisk.Stable, KoronusWarpIncident.None);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        var query = EntityQueryEnumerator<KoronusWarpEncounterComponent, MapGridComponent>();
        while (query.MoveNext(out var shuttle, out var encounter, out var grid))
        {
            if (encounter.Triggered)
            {
                foreach (var owned in encounter.OwnedEntities)
                    if (TryComp<MobStateComponent>(owned, out _) &&
                        TryComp<TransformComponent>(owned, out var transform) && transform.GridUid != shuttle)
                    {
                        encounter.LostBoarder = true;
                        QueueDel(owned);
                    }
                if (_timing.CurTime >= encounter.ExpiresAt)
                {
                    RemCompDeferred<KoronusWarpEncounterComponent>(shuttle);
                    continue;
                }
                if (KoronusWarpRoutePolicy.HasBreach(encounter.Incident) &&
                    !encounter.WaveSpawned && encounter.Rift is { } opening && Exists(opening) &&
                    _timing.CurTime >= encounter.BreachAt)
                {
                    encounter.WaveSpawned = true;
                    SpawnBoarders(shuttle, grid, encounter);
                }
                if (encounter.WaveSpawned && !encounter.Resolved &&
                    !encounter.OwnedEntities.Any(entity => TryComp<MobStateComponent>(entity, out var mob) &&
                        mob.CurrentState != MobState.Dead))
                    FinishBreach(shuttle, encounter);
                continue;
            }

            if (!TryComp<FTLComponent>(shuttle, out var ftl))
            {
                RemCompDeferred<KoronusWarpEncounterComponent>(shuttle);
                continue;
            }
            if (ftl.State != FTLState.Travelling || _timing.CurTime <
                ftl.StateTime.Start + (ftl.StateTime.End - ftl.StateTime.Start) / 4)
                continue;
            encounter.Triggered = true;
            encounter.ExpiresAt = _timing.CurTime + TimeSpan.FromMinutes(2);
            if (!LivingCrew(shuttle).Any())
                continue;
            encounter.Incident = encounter.PlannedIncident;
            if (encounter.Incident == KoronusWarpIncident.None)
                continue;
            var position = Vector2.Zero;
            if (KoronusWarpRoutePolicy.HasBreach(encounter.Incident) &&
                !TryFindDeckPosition(shuttle, grid, out position))
                encounter.Incident = KoronusWarpIncident.Turbulence;

            Announce(shuttle, $"koronus-warp-incident-{encounter.Incident.ToString().ToLowerInvariant()}");
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Ambience/anomaly_drone.ogg"), shuttle);
            if (!KoronusWarpRoutePolicy.HasBreach(encounter.Incident))
            {
                ApplyDisturbance(shuttle, encounter);
                RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
                continue;
            }
            var rift = Spawn("KoronusWarpRift", new EntityCoordinates(shuttle, position));
            EnsureComp<KoronusWarpRiftComponent>(rift).Shuttle = shuttle;
            encounter.Rift = rift;
            encounter.OwnedEntities.Add(rift);
            encounter.BreachAt = _timing.CurTime + TimeSpan.FromSeconds(12);
            RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
        }
    }

    private void ApplyDisturbance(EntityUid shuttle, KoronusWarpEncounterComponent encounter)
    {
        var duration = encounter.Incident switch
        {
            KoronusWarpIncident.Turbulence => 6,
            KoronusWarpIncident.Vertigo => 10,
            KoronusWarpIncident.Blackout => 15,
            _ => 18,
        };
        encounter.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(duration);
        foreach (var player in LivingCrew(shuttle))
        {
            switch (encounter.Incident)
            {
                case KoronusWarpIncident.Turbulence:
                    _jitter.DoJitter(player, TimeSpan.FromSeconds(duration), true, amplitude: 6f);
                    break;
                case KoronusWarpIncident.Vertigo:
                    _statusEffects.TryAddStatusEffect<KoronusWarpVertigoComponent>(player,
                        KoronusWarpVertigoSystem.EffectId, TimeSpan.FromSeconds(duration), true);
                    break;
                case KoronusWarpIncident.PsychicStorm:
                    _statusEffects.TryAddStatusEffect<KoronusWarpVertigoComponent>(player,
                        KoronusWarpVertigoSystem.EffectId, TimeSpan.FromSeconds(duration), true);
                    _stun.TrySlowdown(player, TimeSpan.FromSeconds(duration), true, 0.75f, 0.65f);
                    _jitter.DoJitter(player, TimeSpan.FromSeconds(6), true, amplitude: 4f);
                    break;
            }
        }
        if (encounter.Incident != KoronusWarpIncident.Blackout)
            return;

        encounter.BlackoutUntil = encounter.ExpiresAt;
        var receivers = EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
        while (receivers.MoveNext(out var device, out var receiver, out var transform))
        {
            if (transform.GridUid != shuttle || !receiver.NeedsPower || receiver.PowerDisabled ||
                HasComp<ShuttleConsoleComponent>(device) || HasComp<EmpDisabledComponent>(device))
                continue;
            if (_emp.TryEmpEffects(device, 0f, TimeSpan.FromSeconds(duration)))
                encounter.DisabledEquipment.Add(device);
        }
        if (TryComp<MapGridComponent>(shuttle, out var grid) && TryFindDeckPosition(shuttle, grid, out var position))
        {
            var relay = Spawn("KoronusWarpFieldRelay", new EntityCoordinates(shuttle, position));
            EnsureComp<KoronusWarpRiftComponent>(relay).Shuttle = shuttle;
            encounter.Rift = relay;
            encounter.OwnedEntities.Add(relay);
        }
    }

    private void SpawnBoarders(EntityUid shuttle, MapGridComponent grid, KoronusWarpEncounterComponent encounter)
    {
        foreach (var prototype in KoronusWarpRoutePolicy.GetBoarders(encounter.Incident))
        {
            if (!TryFindDeckPosition(shuttle, grid, out var position))
                break;
            var hostile = Spawn(prototype, new EntityCoordinates(shuttle, position));
            if (!_audit.TryPrepareHostile(hostile, out _))
            {
                QueueDel(hostile);
                continue;
            }
            encounter.OwnedEntities.Add(hostile);
        }
        Announce(shuttle, "koronus-warp-breach");
    }

    private void FinishBreach(EntityUid shuttle, KoronusWarpEncounterComponent encounter)
    {
        encounter.Resolved = true;
        var coordinates = encounter.Rift is { } rift && Exists(rift)
            ? Transform(rift).Coordinates : Transform(shuttle).Coordinates;
        if (encounter.Rift is { } opening)
            QueueDel(opening);
        encounter.Rift = null;
        if (!encounter.LostBoarder && !encounter.SalvageReleased)
        {
            // The salvage belongs to the crew after the breach is closed, independently of its timer.
            if (TryComp<MapGridComponent>(shuttle, out var grid) && TryFindDeckPosition(shuttle, grid, out var position))
                coordinates = new EntityCoordinates(shuttle, position);
            _salvage.SpawnSalvage(encounter.Incident == KoronusWarpIncident.Blackout
                    ? KoronusActivitySalvageProfile.Engineering : KoronusActivitySalvageProfile.Xenology,
                encounter.Risk == KoronusWarpRisk.Unstable ? KoronusActivityRisk.High : KoronusActivityRisk.Moderate,
                coordinates);
            encounter.SalvageReleased = true;
            Announce(shuttle, encounter.Incident == KoronusWarpIncident.Blackout
                ? "koronus-warp-power-restored" : "koronus-warp-salvage");
        }
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
    }

    public bool TryFindDeckPosition(EntityUid shuttle, MapGridComponent grid, out Vector2 position)
    {
        position = default;
        var count = 0;
        var tiles = _maps.GetAllTilesEnumerator(shuttle, grid);
        while (tiles.MoveNext(out var tile))
        {
            if (tile == null || tile.Value.Tile.IsEmpty || _turf.IsSpace(tile.Value) ||
                _turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask))
                continue;
            if (_random.Next(++count) == 0)
                position = _maps.ToCenterCoordinates(tile.Value, grid).Position;
        }
        return count > 0;
    }

    private IEnumerable<EntityUid> LivingCrew(EntityUid shuttle)
    {
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } player && !HasComp<GhostComponent>(player) &&
                TryComp<MobStateComponent>(player, out var state) && state.CurrentState == MobState.Alive &&
                TryComp<TransformComponent>(player, out var transform) && transform.GridUid == shuttle)
                yield return player;
        }
    }

    private void Announce(EntityUid shuttle, string key)
    {
        foreach (var player in LivingCrew(shuttle))
            _popup.PopupEntity(_culture.GetPlayerString(player, key), player, player, PopupType.LargeCaution);
    }

    private void OnInteract(EntityUid uid, KoronusWarpRiftComponent rift, InteractHandEvent args)
    {
        if (args.Handled || !LivingCrew(rift.Shuttle).Contains(args.User))
            return;
        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(4),
            new KoronusWarpSealDoAfterEvent(), uid, target: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnSeal(EntityUid uid, KoronusWarpRiftComponent rift, KoronusWarpSealDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !LivingCrew(rift.Shuttle).Contains(args.User) ||
            !TryComp<KoronusWarpEncounterComponent>(rift.Shuttle, out var encounter) || encounter.Rift != uid)
            return;
        args.Handled = true;
        encounter.Rift = null;
        encounter.WaveSpawned = true;
        QueueDel(uid);
        if (encounter.Incident == KoronusWarpIncident.Blackout)
            RestoreEquipment(encounter);
        if (!encounter.OwnedEntities.Any(entity => TryComp<MobStateComponent>(entity, out var mob) &&
                mob.CurrentState != MobState.Dead))
            FinishBreach(rift.Shuttle, encounter);
        if (encounter.Incident != KoronusWarpIncident.Blackout)
            Announce(rift.Shuttle, "koronus-warp-sealed");
        RaiseLocalEvent(new KoronusActivityPresentationChangedEvent());
    }

    private void OnArrived(EntityUid uid, KoronusWarpEncounterComponent encounter, ref FTLCompletedEvent args)
    {
        if (!encounter.Triggered || encounter.Incident == KoronusWarpIncident.None)
            RemCompDeferred<KoronusWarpEncounterComponent>(uid);
    }

    private void OnShutdown(EntityUid uid, KoronusWarpEncounterComponent encounter, ComponentShutdown args)
    {
        RestoreEquipment(encounter);
        foreach (var entity in encounter.OwnedEntities)
            if (Exists(entity))
                QueueDel(entity);
        encounter.OwnedEntities.Clear();
    }

    private void RestoreEquipment(KoronusWarpEncounterComponent encounter)
    {
        foreach (var device in encounter.DisabledEquipment)
            if (TryComp<EmpDisabledComponent>(device, out var emp) && emp.DisabledUntil == encounter.BlackoutUntil)
                RemCompDeferred<EmpDisabledComponent>(device);
        encounter.DisabledEquipment.Clear();
    }

    private void OnRestart(RoundRestartCleanupEvent args)
    {
        ClearEncounters();
    }

    public void ClearEncounters()
    {
        var query = EntityQueryEnumerator<KoronusWarpEncounterComponent>();
        while (query.MoveNext(out var uid, out _))
            RemCompDeferred<KoronusWarpEncounterComponent>(uid);
    }
}
