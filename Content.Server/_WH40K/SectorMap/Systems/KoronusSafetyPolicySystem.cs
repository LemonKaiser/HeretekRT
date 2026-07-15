using System.Numerics;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._Mono.SpaceArtillery.Components;
using Content.Server._Mono.FireControl;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.Atmos.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Chemistry.Hypospray.Events;
using Content.Shared.Chemistry.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Cuffs.Components;
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
using Robust.Shared.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Server.Atmos.EntitySystems;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// The authoritative Koronus safety policy. A rule can come from a system profile, a planetary
/// surface profile, a static authored circle, or a dynamic zone attached to a facility grid.
/// Infrastructure protection is deliberately separate: only explicit ProtectedGrid entities are
/// made immune, so generated asteroids and procedural terrain remain destructible.
/// </summary>
public sealed class KoronusSafetyPolicySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private ShuttleConsoleLockSystem _shipAccess = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;

    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();

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
    }

    /// <summary>
    /// Checks the union of all authored profiles that contain a point.
    /// </summary>
    public bool HasRule(MapId mapId, Vector2 position, KoronusSafetyRule rule)
    {
        var rules = KoronusSafetyRule.None;

        if (_sector.TryGetSurfaceId(mapId, out var surfaceId) &&
            _prototypes.TryIndex<KoronusPlanetSurfacePrototype>(surfaceId, out var surface) &&
            surface.SafetyProfile is { } surfaceProfile)
        {
            rules |= _prototypes.Index(surfaceProfile).Rules;
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
        var zones = EntityQueryEnumerator<KoronusSafetyZoneComponent, TransformComponent>();
        while (zones.MoveNext(out _, out var zone, out var transform))
        {
            if (transform.MapID != mapId || zone.Radius <= 0f)
                continue;

            var centre = _transform.GetWorldPosition(transform);
            if (Vector2.DistanceSquared(position, centre) <= zone.Radius * zone.Radius)
                rules |= _prototypes.Index(zone.Profile).Rules;
        }

        return (rules & rule) != 0;
    }

    public bool HasRule(EntityUid entity, KoronusSafetyRule rule)
    {
        if (!_transformQuery.TryComp(entity, out var transform))
            return false;

        return HasRule(transform.MapID, _transform.GetWorldPosition(transform), rule);
    }

    /// <summary>
    /// Grid-level checks intentionally use the grid origin. A facility zone protects the whole
    /// explicitly protected grid even if a large machine sits near the edge of its circle.
    /// </summary>
    private bool HasRuleOnGrid(EntityUid grid, KoronusSafetyRule rule)
    {
        return HasRule(grid, rule);
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

            break;
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
        return ShouldBlockPlayerAgainstPlayer(source, target, KoronusSafetyRule.PlayerHarmfulInteractions) ||
               ShouldBlockPlayerAgainstMob(source, target) &&
               (HasRule(target, KoronusSafetyRule.PlayerHarmfulInteractions) ||
                TryResolveResponsiblePlayer(source, out var player) && HasRule(player, KoronusSafetyRule.PlayerHarmfulInteractions));
    }

    public bool ShouldBlockWireModification(EntityUid user, EntityUid target)
    {
        if (!TryGetGrid(target, out var grid))
            return false;

        if (HasComp<ProtectedGridComponent>(grid) && HasRuleOnGrid(grid, KoronusSafetyRule.StationProtection))
            return true;

        return HasComp<ShuttleDeedComponent>(grid) &&
               HasRuleOnGrid(grid, KoronusSafetyRule.PlayerShipWires) &&
               !_shipAccess.HasShipOwnerAccess(grid, user);
    }

    public bool ShouldBlockPlayerShipCollision(EntityUid firstGrid, EntityUid secondGrid)
    {
        if (HasComp<ProtectedGridComponent>(firstGrid) && HasRuleOnGrid(firstGrid, KoronusSafetyRule.StationProtection) ||
            HasComp<ProtectedGridComponent>(secondGrid) && HasRuleOnGrid(secondGrid, KoronusSafetyRule.StationProtection))
            return true;

        return HasComp<ShuttleDeedComponent>(firstGrid) && HasComp<ShuttleDeedComponent>(secondGrid) &&
               (HasRuleOnGrid(firstGrid, KoronusSafetyRule.PlayerDamage) ||
                HasRuleOnGrid(secondGrid, KoronusSafetyRule.PlayerDamage));
    }

    public bool ShouldBlockPlayerShipDamage(EntityUid? source, EntityUid target)
    {
        if (!TryResolveResponsiblePlayer(source, out var player) || !TryGetGrid(target, out var grid))
            return false;

        if (HasComp<ProtectedGridComponent>(grid) && HasRuleOnGrid(grid, KoronusSafetyRule.StationProtection))
            return !HasComp<MobStateComponent>(target);

        return HasComp<ShuttleDeedComponent>(grid) &&
               HasRuleOnGrid(grid, KoronusSafetyRule.PlayerDamage) &&
               !_shipAccess.HasShipOwnerAccess(grid, player);
    }

    public bool ShouldBlockGridModification(EntityUid user, EntityUid target)
    {
        if (!TryGetGrid(target, out var grid))
            return false;

        if (HasComp<ProtectedGridComponent>(grid) && HasRuleOnGrid(grid, KoronusSafetyRule.StationProtection))
            return true;

        return HasComp<ShuttleDeedComponent>(grid) &&
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
        if (HasComp<ProtectedGridComponent>(grid) && HasRuleOnGrid(grid, KoronusSafetyRule.StationProtection))
            return true;

        if (!TryResolveResponsiblePlayer(cause, out var player))
            return false;

        return HasComp<ShuttleDeedComponent>(grid) &&
               HasRuleOnGrid(grid, KoronusSafetyRule.PlayerExplosions) &&
               !_shipAccess.HasShipOwnerAccess(grid, player);
    }

    private void OnShotAttempted(EntityUid uid, GunComponent component, ref ShotAttemptedEvent args)
    {
        if (!HasComp<FireControllableComponent>(uid) && !HasComp<SpaceArtilleryComponent>(uid))
            return;

        if (HasRule(uid, KoronusSafetyRule.ShipWeapons))
            args.Cancel();
    }

    private void OnBeforeDamageChanged(EntityUid uid, DamageableComponent component, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || args.Damage.GetTotal() <= 0)
            return;

        if (ShouldBlockPlayerAgainstPlayer(args.Origin, uid, KoronusSafetyRule.PlayerDamage) ||
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
        var source = args.User ?? args.Source;
        if (ShouldBlockHarmfulInteraction(source, uid))
            args.Cancelled = true;
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
