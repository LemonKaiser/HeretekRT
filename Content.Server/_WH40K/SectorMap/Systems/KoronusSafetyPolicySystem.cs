using System.Numerics;
using Content.Server._WH40K.Progression;
using Content.Server._WH40K.ClassProgression;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.OperationalDomain;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server.Atmos.Components;
using Content.Server.Fluids.Components;
using Content.Server.IgnitionSource;
using Content.Server._Mono.SpaceArtillery.Components;
using Content.Server._Mono.FireControl;
using Content.Server.Shuttles.Systems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Mono;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared._WH40K.ClassProgression;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Events;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Organ;
using Content.Shared.Item;
using Content.Shared.Chemistry.Hypospray.Events;
using Content.Shared.Chemistry.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Cuffs.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Flash;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Pulling.Events;
using Content.Shared.Projectiles;
using Content.Shared.Strip.Components;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Tiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Damage.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Disposal.Components;
using Content.Shared.DragDrop;
using Content.Shared.Fluids;
using Content.Shared.Interaction.Components;
using Content.Shared.IgnitionSource;
using Content.Shared.Popups;
using Content.Shared.Radiation.Components;
using Content.Shared.Temperature;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.Player;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// The authoritative Koronus safety policy. A rule can come from a system profile, a planetary
/// surface profile, a static authored circle, or a dynamic zone attached to a facility grid.
/// Infrastructure protection is deliberately separate: only authored facility grids are
/// made immune, so generated asteroids and procedural terrain remain destructible.
/// </summary>
public sealed class KoronusSafetyPolicySystem : EntitySystem
{
    private static readonly TimeSpan EnvironmentalSafetySweepInterval = TimeSpan.FromMilliseconds(250);

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private ShuttleConsoleLockSystem _shipAccess = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WH40KPlayerCultureManager _playerCulture = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kPartyManager _wh40kParties = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private SharedIgnitionSourceSystem _ignitionSource = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private OperationalDomainSystem _domains = default!;

    private EntityQuery<TransformComponent> _transformQuery;
    private TimeSpan _nextEnvironmentalSafetySweep;
    private readonly HashSet<EntityUid> _checkedDockGrids = new();
    private readonly Stack<EntityUid> _dockGrids = new();

    public override void Initialize()
    {
        base.Initialize();

        // Process sources before they can create another atmospheric hotspot, but sweep
        // entities after the normal flammability update has handled legacy fire state.
        UpdatesAfter.Add(typeof(FlammableSystem));
        UpdatesBefore.Add(typeof(IgnitionSourceSystem));
        UpdatesBefore.Add(typeof(TemperatureSystem));

        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<StaminaComponent, BeforeStaminaDamageEvent>(OnBeforeStaminaDamage);
        SubscribeLocalEvent<PullableComponent, BeingPulledAttemptEvent>(OnBeingPulledAttempt);
        SubscribeLocalEvent<StrippableComponent, StripAttemptEvent>(OnStripAttempt);
        SubscribeLocalEvent<BuckleComponent, BuckleAttemptEvent>(OnBuckleAttempt);
        SubscribeLocalEvent<BuckleComponent, UnbuckleAttemptEvent>(OnUnbuckleAttempt);
        SubscribeLocalEvent<CuffableComponent, CuffAttemptEvent>(OnCuffAttempt);
        SubscribeLocalEvent<DamageableComponent, DisarmAttemptEvent>(OnDisarmAttempt);
        SubscribeLocalEvent<DamageableComponent, FlashAttemptEvent>(OnFlashAttempt);
        SubscribeLocalEvent<DamageableComponent, TargetBeforeHyposprayInjectsEvent>(OnHyposprayAttempt);
        SubscribeLocalEvent<DamageableComponent, InjectorTargetAttemptEvent>(OnInjectorAttempt);
        SubscribeLocalEvent<DamageableComponent, ForceFeedAttemptEvent>(OnForceFeedAttempt);
        SubscribeLocalEvent<FlammableComponent, TryIgniteEvent>(OnIgniteAttempt);
        SubscribeLocalEvent<GunComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<AnchorableComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<AnchorableComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, HandheldEntityPlacementAttemptEvent>(OnHandheldEntityPlacementAttempt);
        SubscribeLocalEvent<SprayComponent, SprayAttemptEvent>(OnSprayAttempt);
        SubscribeLocalEvent<BodyComponent, DragDropDraggedEvent>(OnBodyDragDrop);
        SubscribeLocalEvent<GasTankComponent, GasTankValveAttemptEvent>(OnGasTankValveAttempt);
        SubscribeLocalEvent<GasCanisterComponent, GasCanisterValveAttemptEvent>(OnGasCanisterValveAttempt);
        SubscribeLocalEvent<TemperatureComponent, ModifyChangedTemperatureEvent>(OnTemperatureChangeAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // These queries cover every fire-capable entity and atmosphere grid. The event gates above
        // still reject new ignition and heat immediately, so a short periodic cleanup is enough for
        // legacy state without performing the same full-world scans every frame.
        if (_timing.CurTime < _nextEnvironmentalSafetySweep)
            return;

        _nextEnvironmentalSafetySweep = _timing.CurTime + EnvironmentalSafetySweepInterval;
        SuppressProtectedIgnitionSources();
        SuppressProtectedCombustion();
        SuppressProtectedTemperatures();
        SuppressProtectedHotspots();
    }

    /// <summary>
    /// Checks the union of all authored profiles that contain a point.
    /// </summary>
    public bool HasRule(MapId mapId, Vector2 position, KoronusSafetyRule rule)
    {
        return (GetAreaRules(mapId, position) & rule) != 0;
    }

    private KoronusSafetyRule GetAreaRules(MapId mapId, Vector2 position)
    {
        var rules = KoronusSafetyRule.None;

        if (_sector.TryGetSurfaceId(mapId, out var surfaceId) &&
            _prototypes.TryIndex<KoronusPlanetSurfacePrototype>(surfaceId, out var surface) &&
            surface.SafetyProfile is { } surfaceProfile)
        {
            rules |= _prototypes.Index(surfaceProfile).Rules;
            if (surface.PreserveHostileNpcs)
                rules &= ~KoronusSafetyRule.HostileNpcAutoCleanup;
        }
        else if (_sector.TryGetSystemId(mapId, out var systemId) &&
                 _sector.TryGetSystemPrototype(systemId, out var system))
        {
            if (system.SafetyProfile is { } systemProfile)
                rules |= _prototypes.Index(systemProfile).Rules;

            foreach (var zone in system.SafetyZones)
            {
                if (zone.Radius <= 0f || Vector2.DistanceSquared(position, zone.Center) > zone.Radius * zone.Radius)
                    continue;

                rules |= _prototypes.Index(zone.Profile).Rules;
            }
        }

        // Dynamic zones are anchored to the facility grid and therefore follow random placement,
        // docking and grid movement instead of relying on stale map coordinates.
        // Preloaded stations are paused, but their safety rules must already apply when
        // bootstrap removes hostile map spawns before the first player arrives.
        var zones = AllEntityQuery<KoronusSafetyZoneComponent, TransformComponent>();
        while (zones.MoveNext(out _, out var zone, out var transform))
        {
            if (transform.MapID != mapId || zone.Radius <= 0f)
                continue;

            var centre = _transform.GetWorldPosition(transform);
            if (Vector2.DistanceSquared(position, centre) <= zone.Radius * zone.Radius)
                rules |= _prototypes.Index(zone.Profile).Rules;
        }

        return rules;
    }

    public bool HasRule(EntityUid entity, KoronusSafetyRule rule)
    {
        return (GetRules(entity) & rule) != 0;
    }

    /// <summary>
    /// Excludes safe areas, protected facilities and their docked vessels from space NPC targets.
    /// Bosses additionally restrict their targets to independent vessels.
    /// </summary>
    public bool IsProtectedSpaceTarget(EntityUid target, bool requireVessel = false)
    {
        if (TerminatingOrDeleted(target) || !TryComp(target, out TransformComponent? xform))
            return true;

        const KoronusSafetyRule rules = KoronusSafetyRule.StationProtection |
                                       KoronusSafetyRule.HostileNpcAutoCleanup |
                                       KoronusSafetyRule.ShipWeapons;
        if (HasRule(target, rules))
            return true;

        var grid = HasComp<MapGridComponent>(target) ? target : xform.GridUid;
        if (grid is not { } gridUid)
            return false;

        // Follow the full docking chain so a vessel cannot expose station occupants as targets.
        _checkedDockGrids.Clear();
        _dockGrids.Clear();
        _dockGrids.Push(gridUid);
        while (_dockGrids.TryPop(out var connected))
        {
            if (!_checkedDockGrids.Add(connected) || TerminatingOrDeleted(connected))
                continue;
            if (HasComp<ProtectedGridComponent>(connected) || HasComp<KoronusProtectedFacilityComponent>(connected) ||
                requireVessel && !_domains.IsVesselGrid(connected) || HasRule(connected, rules))
                return true;
            foreach (var dock in _docking.GetDocks(connected))
            {
                if (TryComp(dock.Comp.DockedWith, out TransformComponent? other) && other.GridUid is { } otherGrid)
                    _dockGrids.Push(otherGrid);
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the additive rule set active at an entity, including its grid profile.
    /// </summary>
    public KoronusSafetyRule GetRules(EntityUid entity)
    {
        if (!_transformQuery.TryComp(entity, out var transform))
            return KoronusSafetyRule.None;

        var rules = GetAreaRules(transform.MapID, _transform.GetWorldPosition(transform));
        if (TryComp<KoronusGridSafetyProfileComponent>(entity, out var ownGridProfile))
            rules |= _prototypes.Index(ownGridProfile.Profile).Rules;
        else if (transform.GridUid is { } grid &&
                 TryComp<KoronusGridSafetyProfileComponent>(grid, out var gridProfile))
        {
            rules |= _prototypes.Index(gridProfile.Profile).Rules;
        }

        // Portable containers can be bought or brought in after facility initialization.
        // Their release restriction follows the protected grid, not an optional local profile.
        if (TryGetGrid(entity, out var protectedGrid) && IsFacilityGrid(protectedGrid))
            rules |= KoronusSafetyRule.AtmosphericRelease;

        return rules;
    }

    /// <summary>
    /// Generic first-line gate for class Actions. Concrete handlers must call this again with their actual target.
    /// </summary>
    public bool IsClassActionAllowed(
        EntityUid source,
        EntityUid? target,
        Wh40kClassEffectSafety safety)
    {
        var sourceRules = GetRules(source);
        var targetRules = target is { Valid: true } targetUid
            ? GetRules(targetUid)
            : KoronusSafetyRule.None;
        var targetIsPlayer = target is { Valid: true } player && IsPlayerCharacter(player);
        var targetIsProtectedNpc = safety == Wh40kClassEffectSafety.NpcOnly &&
                                   target is { Valid: true } npc &&
                                   !targetIsPlayer &&
                                   ShouldBlockPlayerAgainstMob(source, npc);
        if (safety == Wh40kClassEffectSafety.DeviceInteraction && IsFootfallSpace(source))
        {
            if (target is { Valid: true } device &&
                (ShouldBlockWireModification(source, device) || ShouldBlockGridModification(source, device)))
                return false;

            // Ship ownership restrictions apply to the selected device, not every device in the sector.
            const KoronusSafetyRule shipRules = KoronusSafetyRule.PlayerShipWires | KoronusSafetyRule.PlayerShipDeconstruction;
            sourceRules &= ~shipRules;
            targetRules &= ~shipRules;
        }
        return Wh40kClassRuntimePolicy.IsClassActionAllowed(
            safety,
            sourceRules,
            targetRules,
            targetIsPlayer,
            targetIsProtectedNpc,
            target == null || target == source);
    }

    /// <summary>
    /// Grid-level checks intentionally use the grid origin. A facility zone protects the whole
    /// explicitly protected grid even if a large machine sits near the edge of its circle.
    /// </summary>
    private bool HasRuleOnGrid(EntityUid grid, KoronusSafetyRule rule)
    {
        return HasRule(grid, rule);
    }

    public bool IsFootfallSpace(EntityUid entity)
    {
        return _transformQuery.TryComp(entity, out var transform) &&
               _sector.TryGetSystemId(transform.MapID, out var systemId) && systemId == "Footfall";
    }

    private bool IsFacilityGrid(EntityUid grid)
    {
        return IsFootfallSpace(grid)
            ? HasComp<KoronusProtectedFacilityComponent>(grid)
            : HasComp<ProtectedGridComponent>(grid);
    }

    private bool IsProtectedFacility(EntityUid grid)
    {
        return IsFacilityGrid(grid) && HasRuleOnGrid(grid, KoronusSafetyRule.StationProtection);
    }

    private bool IsPlayerShip(EntityUid grid)
    {
        if (!IsFootfallSpace(grid))
            return HasComp<ShuttleDeedComponent>(grid);

        if (HasComp<KoronusMiningAsteroidComponent>(grid))
            return false;

        return TryComp<ShipOwnershipComponent>(grid, out var ownership) && ownership.OwnerUserId != default ||
               TryComp<ShuttleDeedComponent>(grid, out var deed) && deed.ShuttleUid == grid &&
               !string.IsNullOrEmpty(deed.ShuttleOwner) && deed.ShuttleOwner != "Unknown";
    }

    /// <summary>
    /// A mind is the authoritative player marker. This covers borgs, polymorphs and non-humanoid
    /// player bodies while still excluding ordinary NPCs and fauna.
    /// </summary>
    public bool IsPlayerCharacter(EntityUid entity)
    {
        return TryComp<MindContainerComponent>(entity, out var mind) && mind.HasMind;
    }

    /// <summary>
    /// Resolves direct, projectile and thrown-item sources. Automatic ship guns store their user
    /// in AutoShootGunComponent so delayed shots keep their attribution as well.
    /// </summary>
    public bool TryResolveResponsiblePlayer(EntityUid? source, out EntityUid player)
    {
        return TryResolveResponsibleActor(source, out player) && IsPlayerCharacter(player);
    }

    private bool TryResolveResponsibleActor(EntityUid? source, out EntityUid player)
    {
        player = EntityUid.Invalid;
        if (source is not { Valid: true } current)
            return false;

        var visited = new HashSet<EntityUid>();
        for (var depth = 0; depth < 8 && visited.Add(current); depth++)
        {
            if (IsPlayerCharacter(current))
            {
                player = current;
                return true;
            }

            if (TryComp<ProjectileComponent>(current, out var projectile) && projectile.Shooter is { Valid: true } shooter)
            {
                current = shooter;
                continue;
            }

            if (TryComp<ThrownItemComponent>(current, out var thrown) && thrown.Thrower is { Valid: true } thrower)
            {
                current = thrower;
                continue;
            }

            if (TryComp<AutoShootGunComponent>(current, out var gun) && gun.Shooter is { Valid: true } gunner)
            {
                current = gunner;
                continue;
            }

            player = current;
            return true;
        }

        return false;
    }

    public bool ShouldBlockPlayerAgainstPlayer(EntityUid? source, EntityUid target, KoronusSafetyRule rule)
    {
        if (!IsPlayerCharacter(target) || !TryResolveResponsiblePlayer(source, out var player) || player == target)
            return false;

        return HasRule(player, rule) || HasRule(target, rule);
    }

    /// <summary>
    /// Returns true when a non-player mob is not hostile to the attacking player. Missing faction
    /// data is treated as peaceful, which is the safe default for pets and authored fauna.
    /// </summary>
    private bool IsNonHostileMobToPlayer(EntityUid target, EntityUid player)
    {
        if (IsPlayerCharacter(target) || !HasComp<MobStateComponent>(target))
            return false;

        if (!TryComp<NpcFactionMemberComponent>(target, out var targetFaction) ||
            !TryComp<NpcFactionMemberComponent>(player, out var playerFaction))
            return true;

        foreach (var targetId in targetFaction.Factions)
        {
            foreach (var playerId in playerFaction.Factions)
            {
                if (_npcFaction.IsFactionHostile(targetId.Id, playerId.Id) ||
                    _npcFaction.IsFactionHostile(playerId.Id, targetId.Id))
                    return false;
            }
        }

        return true;
    }

    public bool ShouldBlockPlayerAgainstMob(EntityUid? source, EntityUid target)
    {
        if (!TryResolveResponsiblePlayer(source, out var player) ||
            !IsNonHostileMobToPlayer(target, player))
            return false;

        return HasRule(player, KoronusSafetyRule.ProtectNonHostileMobs) ||
               HasRule(target, KoronusSafetyRule.ProtectNonHostileMobs);
    }

    public bool ShouldBlockHarmfulInteraction(EntityUid? source, EntityUid target)
    {
        return ShouldBlockNpcAgainstPlayer(source, target) ||
               ShouldBlockPlayerAgainstPlayer(source, target, KoronusSafetyRule.PlayerHarmfulInteractions) ||
               ShouldBlockPlayerAgainstMob(source, target) &&
               (HasRule(target, KoronusSafetyRule.PlayerHarmfulInteractions) ||
                TryResolveResponsiblePlayer(source, out var player) && HasRule(player, KoronusSafetyRule.PlayerHarmfulInteractions));
    }

    public bool ShouldBlockNpcAgainstPlayer(EntityUid? source, EntityUid target)
    {
        if (TryComp<BodyPartComponent>(target, out var part) && part.Body is { } partBody)
            target = partBody;
        else if (TryComp<OrganComponent>(target, out var organ) && organ.Body is { } organBody)
            target = organBody;

        return IsPlayerCharacter(target) && IsFootfallSpace(target) &&
               TryGetGrid(target, out var grid) && IsProtectedFacility(grid) &&
               TryResolveResponsibleActor(source, out var actor) &&
               !IsPlayerCharacter(actor) && HasComp<MobStateComponent>(actor);
    }

    public bool ShouldBlockWireModification(EntityUid user, EntityUid target)
    {
        if (!TryGetGrid(target, out var grid))
            return false;

        if (IsProtectedFacility(grid))
            return true;

        return IsPlayerShip(grid) &&
               HasRuleOnGrid(grid, KoronusSafetyRule.PlayerShipWires) &&
               !_shipAccess.HasShipOwnerAccess(grid, user);
    }

    public bool ShouldBlockPlayerShipCollision(EntityUid firstGrid, EntityUid secondGrid)
    {
        if (IsProtectedFacility(firstGrid) || IsProtectedFacility(secondGrid) ||
            IsFootfallSpace(firstGrid) && IsPlayerShip(firstGrid) ||
            IsFootfallSpace(secondGrid) && IsPlayerShip(secondGrid))
            return true;

        return HasComp<ShuttleDeedComponent>(firstGrid) && HasComp<ShuttleDeedComponent>(secondGrid) &&
               (HasRuleOnGrid(firstGrid, KoronusSafetyRule.PlayerDamage) ||
                HasRuleOnGrid(secondGrid, KoronusSafetyRule.PlayerDamage));
    }

    public bool ShouldBlockPlayerShipDamage(EntityUid? source, EntityUid target)
    {
        if (!TryGetGrid(target, out var grid))
            return false;

        if (IsFootfallSpace(grid))
        {
            // Protect infrastructure without making passengers, their organs or carried items invulnerable.
            return (IsProtectedFacility(grid) || IsPlayerShip(grid)) &&
                   !HasComp<MobStateComponent>(target) && !HasComp<BodyComponent>(target) &&
                   !HasComp<BodyPartComponent>(target) && !HasComp<OrganComponent>(target) &&
                   (!HasComp<ItemComponent>(target) || Transform(target).Anchored);
        }

        if (!TryResolveResponsiblePlayer(source, out var player))
            return false;

        if (IsProtectedFacility(grid))
            return !HasComp<MobStateComponent>(target);

        return HasComp<ShuttleDeedComponent>(grid) &&
               HasRuleOnGrid(grid, KoronusSafetyRule.PlayerDamage) &&
               !_shipAccess.HasShipOwnerAccess(grid, player);
    }

    public bool ShouldBlockGridModification(EntityUid user, EntityUid target)
    {
        if (!TryGetGrid(target, out var grid))
            return false;

        if (HasRuleOnGrid(grid, KoronusSafetyRule.Deconstruction))
            return true;

        if (IsProtectedFacility(grid))
            return true;

        return IsPlayerShip(grid) &&
               HasRuleOnGrid(grid, KoronusSafetyRule.PlayerShipDeconstruction) &&
               !_shipAccess.HasShipOwnerAccess(grid, user);
    }

    // Kept as a compatibility wrapper for existing construction/RCD callers.
    public bool ShouldBlockPlayerShipDeconstruction(EntityUid user, EntityUid target)
    {
        return ShouldBlockGridModification(user, target);
    }

    /// <summary>
    /// ProtectedGrid is an explicit infrastructure marker. It is checked for every affected grid,
    /// not only when the explosion epicentre happens to have that grid as its coordinate parent.
    /// </summary>
    public bool ShouldBlockExplosionGridDamage(EntityUid? cause, EntityUid grid)
    {
        if (IsProtectedFacility(grid) || IsFootfallSpace(grid) && IsPlayerShip(grid))
            return true;

        if (!TryResolveResponsiblePlayer(cause, out var player))
            return false;

        return HasComp<ShuttleDeedComponent>(grid) &&
               HasRuleOnGrid(grid, KoronusSafetyRule.PlayerExplosions) &&
               !_shipAccess.HasShipOwnerAccess(grid, player);
    }

    private void OnShotAttempted(EntityUid uid, GunComponent component, ref ShotAttemptedEvent args)
    {
        if (HasRule(uid, KoronusSafetyRule.RadiationMunitions) &&
            _guns.TryNextShootPrototype((uid, component), out var ammoPrototype) &&
            _guns.GetBulletPrototype(ammoPrototype).TryGetComponent<RadiationSourceComponent>(out _, EntityManager.ComponentFactory))
        {
            args.Cancel();
            _popup.PopupCursor(_playerCulture.GetPlayerString(args.User, "koronus-safety-radiation-ammo-blocked"), args.User);
            return;
        }

        if (!HasComp<FireControllableComponent>(uid) && !HasComp<SpaceArtilleryComponent>(uid))
            return;

        if (HasRule(uid, KoronusSafetyRule.ShipWeapons))
            args.Cancel();
    }

    private void OnBeforeDamageChanged(EntityUid uid, DamageableComponent component, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled)
            return;

        // The normal damage gate can attribute attacks to players, but fire and thermal
        // damage are often environmental and deliberately have no origin. Remove Heat
        // directly so burning, atmospheric heat and delayed effects cannot bypass a
        // protected station through that missing attribution.
        if (HasRule(uid, KoronusSafetyRule.FireAndHighTemperature) &&
            args.Damage.DamageDict.Remove("Heat") &&
            args.Damage.Empty)
        {
            args.Cancelled = true;
            return;
        }

        if (_players.TryGetSessionByEntity(uid, out var target))
        {
            NetUserId? attacker = args.Origin is { Valid: true } origin &&
                                  _players.TryGetSessionByEntity(origin, out var attackerSession)
                ? attackerSession.UserId
                : null;
            if (Wh40kExperiencePolicy.ShouldBlockDirectDamage(
                    args.Damage.GetTotal(),
                    args.OriginFlag == null,
                    attacker,
                    target.UserId,
                    _wh40kParties.AreInSameActiveParty))
            {
                args.Cancelled = true;
                return;
            }
        }

        if (args.Damage.GetTotal() <= 0)
            return;

        if (ShouldBlockPlayerAgainstPlayer(args.Origin, uid, KoronusSafetyRule.PlayerDamage) ||
            ShouldBlockNpcAgainstPlayer(args.Origin, uid) ||
            ShouldBlockPlayerAgainstMob(args.Origin, uid) ||
            ShouldBlockPlayerShipDamage(args.Origin, uid))
        {
            args.Cancelled = true;
        }
    }

    private void OnBeforeStaminaDamage(EntityUid uid, StaminaComponent component, ref BeforeStaminaDamageEvent args)
    {
        if (args.Cancelled || args.Value <= 0f)
            return;

        if (ShouldBlockPlayerAgainstPlayer(args.Source, uid, KoronusSafetyRule.PlayerStaminaDamage) ||
            ShouldBlockNpcAgainstPlayer(args.Source, uid) ||
            ShouldBlockPlayerAgainstMob(args.Source, uid))
            args.Cancelled = true;
    }

    private void OnBeingPulledAttempt(EntityUid uid, PullableComponent component, BeingPulledAttemptEvent args)
    {
        if (!args.Cancelled && ShouldBlockPlayerAgainstPlayer(args.Puller, args.Pulled, KoronusSafetyRule.PlayerPulling))
            args.Cancel();
    }

    private void OnStripAttempt(EntityUid uid, StrippableComponent component, StripAttemptEvent args)
    {
        if (ShouldBlockPlayerAgainstPlayer(args.Actor, args.Target, KoronusSafetyRule.OtherPlayerStrip))
            args.Cancel();
    }

    private void OnBuckleAttempt(EntityUid uid, BuckleComponent component, ref BuckleAttemptEvent args)
    {
        if (!args.Cancelled && args.User != null &&
            ShouldBlockPlayerAgainstPlayer(args.User, uid, KoronusSafetyRule.ForcedBuckle))
            args.Cancelled = true;
    }

    private void OnUnbuckleAttempt(EntityUid uid, BuckleComponent component, ref UnbuckleAttemptEvent args)
    {
        if (!args.Cancelled && args.User != null &&
            ShouldBlockPlayerAgainstPlayer(args.User, uid, KoronusSafetyRule.ForcedUnbuckle))
            args.Cancelled = true;
    }

    private void OnCuffAttempt(EntityUid uid, CuffableComponent component, ref CuffAttemptEvent args)
    {
        if (ShouldBlockHarmfulInteraction(args.User, args.Target))
            args.Cancel();
    }

    private void OnDisarmAttempt(EntityUid uid, DamageableComponent component, DisarmAttemptEvent args)
    {
        if (ShouldBlockHarmfulInteraction(args.DisarmerUid, args.TargetUid))
            args.Cancel();
    }

    private void OnFlashAttempt(EntityUid uid, DamageableComponent component, ref FlashAttemptEvent args)
    {
        if (ShouldBlockHarmfulInteraction(args.User, args.Target))
            args.Cancelled = true;
    }

    private void OnHyposprayAttempt(EntityUid uid, DamageableComponent component, TargetBeforeHyposprayInjectsEvent args)
    {
        if (ShouldBlockHarmfulInteraction(args.EntityUsingHypospray, args.TargetGettingInjected))
            args.Cancel();
    }

    private void OnInjectorAttempt(EntityUid uid, DamageableComponent component, ref InjectorTargetAttemptEvent args)
    {
        if (ShouldBlockHarmfulInteraction(args.User, args.Target))
            args.Cancel();
    }

    private void OnForceFeedAttempt(EntityUid uid, DamageableComponent component, ref ForceFeedAttemptEvent args)
    {
        if (ShouldBlockHarmfulInteraction(args.User, args.Target))
            args.Cancel();
    }

    private void OnIgniteAttempt(EntityUid uid, FlammableComponent component, ref TryIgniteEvent args)
    {
        if (HasRule(uid, KoronusSafetyRule.FireAndHighTemperature))
        {
            args.Cancelled = true;
            SuppressCombustion(uid, component);
            return;
        }

        var source = args.User ?? args.Source;
        if (ShouldBlockHarmfulInteraction(source, uid))
            args.Cancelled = true;
    }

    private void OnTemperatureChangeAttempt(
        Entity<TemperatureComponent> entity,
        ref ModifyChangedTemperatureEvent args)
    {
        if (args.TemperatureDelta > 0f && HasRule(entity.Owner, KoronusSafetyRule.FireAndHighTemperature))
            args.TemperatureDelta = 0f;
    }

    private void SuppressProtectedIgnitionSources()
    {
        var sources = EntityQueryEnumerator<IgnitionSourceComponent>();
        while (sources.MoveNext(out var uid, out var source))
        {
            if (source.Ignited && HasRule(uid, KoronusSafetyRule.FireAndHighTemperature))
                _ignitionSource.SetIgnited((uid, source), false);
        }
    }

    private void SuppressProtectedCombustion()
    {
        var flammables = EntityQueryEnumerator<FlammableComponent>();
        while (flammables.MoveNext(out var uid, out var flammable))
        {
            if (HasRule(uid, KoronusSafetyRule.FireAndHighTemperature))
                SuppressCombustion(uid, flammable);
        }
    }

    private void SuppressProtectedTemperatures()
    {
        var temperatures = EntityQueryEnumerator<TemperatureComponent>();
        while (temperatures.MoveNext(out var uid, out var temperature))
        {
            if (!HasRule(uid, KoronusSafetyRule.FireAndHighTemperature))
                continue;

            var threshold = temperature.ParentHeatDamageThreshold ?? temperature.HeatDamageThreshold;
            if (temperature.CurrentTemperature >= threshold)
                _temperature.ForceChangeTemperature(uid, threshold - 0.01f, temperature);
        }
    }

    private void SuppressCombustion(EntityUid uid, FlammableComponent flammable)
    {
        if (!flammable.OnFire && flammable.FireStacks <= 0f)
            return;

        var wasOnFire = flammable.OnFire;
        _flammable.Extinguish(uid, flammable);

        // A few decorative fire sources deliberately opt out of normal extinguishing.
        // A safety profile is stronger than that content flag: it must leave no flame
        // behind that can light another entity or atmospheric gas.
        if (flammable.OnFire || flammable.FireStacks > 0f)
        {
            flammable.OnFire = false;
            flammable.FireStacks = 0f;
            flammable.ResistCompleteTime = null;
            _ignitionSource.SetIgnited(uid, false);

            if (wasOnFire)
            {
                var extinguished = new ExtinguishedEvent();
                RaiseLocalEvent(uid, ref extinguished);
            }

            _flammable.UpdateAppearance(uid, flammable);
        }
    }

    private void SuppressProtectedHotspots()
    {
        var grids = EntityQueryEnumerator<GridAtmosphereComponent>();
        while (grids.MoveNext(out var gridUid, out var gridAtmosphere))
        {
            foreach (var hotspot in gridAtmosphere.HotspotTiles)
            {
                var coordinates = _mapSystem.ToCenterCoordinates(gridUid, hotspot.GridIndices);
                var mapCoordinates = _transform.ToMapCoordinates(coordinates);
                if (!HasRule(mapCoordinates.MapId, mapCoordinates.Position, KoronusSafetyRule.FireAndHighTemperature))
                    continue;

                _atmosphere.HotspotExtinguish(gridUid, hotspot.GridIndices);

                // Hot plasma or tritium would otherwise recreate the hotspot next tick.
                // Normalizing the tile temperature makes the safe zone stable without
                // modifying gas composition outside of its protected boundary.
                if (_atmosphere.GetTileMixture(gridUid, null, hotspot.GridIndices, excite: true) is { } mixture &&
                    mixture.Temperature > Atmospherics.T20C)
                {
                    mixture.Temperature = Atmospherics.T20C;
                }
            }
        }
    }

    private void OnAnchorAttempt(EntityUid uid, AnchorableComponent component, AnchorAttemptEvent args)
    {
        BlockAnchoring(uid, args.User, args);
    }

    private void OnUnanchorAttempt(EntityUid uid, AnchorableComponent component, UnanchorAttemptEvent args)
    {
        BlockAnchoring(uid, args.User, args);
    }

    private void OnHandheldEntityPlacementAttempt(
        Entity<HandheldEntityPlacementComponent> entity,
        ref HandheldEntityPlacementAttemptEvent args)
    {
        var grid = _transform.GetGrid(args.Coordinates);
        if (args.Cancelled || grid == null || !HasRule(grid.Value, KoronusSafetyRule.HandheldEntityPlacement))
            return;

        args.Cancel();
        _popup.PopupCursor(_playerCulture.GetPlayerString(args.User, "koronus-safety-deployment-blocked"), args.User);
    }

    private void OnSprayAttempt(Entity<SprayComponent> entity, ref SprayAttemptEvent args)
    {
        if (args.Cancelled || !HasRule(args.User, KoronusSafetyRule.ChemicalEffects))
            return;

        args.Cancel();
        _popup.PopupCursor(_playerCulture.GetPlayerString(args.User, "koronus-safety-chemistry-blocked"), args.User);
    }

    private void OnBodyDragDrop(Entity<BodyComponent> entity, ref DragDropDraggedEvent args)
    {
        if (args.Handled ||
            !IsPlayerCharacter(entity.Owner) ||
            !HasComp<DisposalUnitComponent>(args.Target) ||
            !HasRule(args.Target, KoronusSafetyRule.PlayerDisposal))
        {
            return;
        }

        args.Handled = true;
        _popup.PopupCursor(_playerCulture.GetPlayerString(args.User, "koronus-safety-disposal-blocked"), args.User);
    }

    private void OnGasTankValveAttempt(Entity<GasTankComponent> entity, ref GasTankValveAttemptEvent args)
    {
        if (args.Cancelled || !args.Open || !HasRule(entity.Owner, KoronusSafetyRule.AtmosphericRelease))
            return;

        args.Cancel();
        if (args.User is { } user)
            _popup.PopupCursor(_playerCulture.GetPlayerString(user, "koronus-safety-atmospheric-release-blocked"), user);
    }

    private void OnGasCanisterValveAttempt(Entity<GasCanisterComponent> entity, ref GasCanisterValveAttemptEvent args)
    {
        if (args.Cancelled || !args.Open || !HasRule(entity.Owner, KoronusSafetyRule.AtmosphericRelease))
            return;

        args.Cancel();
        if (args.User is { } user)
            _popup.PopupCursor(_playerCulture.GetPlayerString(user, "koronus-safety-atmospheric-release-blocked"), user);
    }

    private void BlockAnchoring(EntityUid target, EntityUid user, BaseAnchoredAttemptEvent args)
    {
        if (args.Cancelled || !HasRule(target, KoronusSafetyRule.Anchoring))
            return;

        args.Cancel();
        _popup.PopupCursor(_playerCulture.GetPlayerString(user, "koronus-safety-anchoring-blocked"), user);
    }

    private bool TryGetGrid(EntityUid entity, out EntityUid grid)
    {
        if (HasComp<MapGridComponent>(entity))
        {
            grid = entity;
            return true;
        }

        if (TryComp<TransformComponent>(entity, out var transform) && transform.GridUid is { } gridUid)
        {
            grid = gridUid;
            return true;
        }

        grid = EntityUid.Invalid;
        return false;
    }
}
