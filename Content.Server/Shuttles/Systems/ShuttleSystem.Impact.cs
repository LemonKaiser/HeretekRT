using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono;
using Content.Shared._Mono.CCVar;
using Content.Shared.Atmos.Components;
using Content.Shared.Audio;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.Events;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Server.Physics;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    [Dependency] private SpaceCleanupSystem _sweep = default!;
    [Dependency] private KoronusSafetyPolicySystem _koronusSafety = default!;
    [Dependency] private GridFixtureSystem _gridFixtures = default!;

    private bool _enabled;
    private float _minimumImpactInertia;
    private float _minimumImpactVelocity;
    private float _tileBreakEnergyMultiplier;
    private float _damageMultiplier;
    private float _structuralDamage;
    private float _sparkEnergy;
    private float _impactRadius;
    private float _impactSlowdown;
    private float _minThrowVelocity;
    private float _inertiaScaling;
    private float _platingMass;
    private float _sweepAggression;
    private float _sweepDelay;
    private float _sweepRadius;
    private float _contactMergeDistance;
    private float _throwRadiusPadding;
    private float _fragmentCleanupAcceleration;
    private float _fragmentMinimumLifetime;
    private float _tunnelMinSpeed;
    private int _maxTilesPerTick;
    private int _maxGridBatchesPerTick;
    private int _maxVisualEffects;
    private int _fragmentCleanupTiles;

    private const float SparkChance = 0.2f;
    private const float BaseShuttleMass = 50f;
    private const float MinImpulseVelocity = 0.07f;
    private readonly TimeSpan _adminLogSpacing = TimeSpan.FromSeconds(3);

    private readonly SoundCollectionSpecifier _shuttleImpactSound = new("ShuttleImpactSound");
    private readonly ProtoId<ContentTileDefinition> _platingId = "Plating";

    private EntityQuery<DamageableComponent> _dmgQuery;
    private EntityQuery<ProjectileComponent> _projQuery;

    private readonly Dictionary<ImpactPairKey, List<ImpactCandidate>> _contactClusters = new();
    private readonly Dictionary<EntityUid, PendingGridDestruction> _pendingDestruction = new();
    private readonly List<ImpactPairKey> _orderedPairs = new();
    private readonly List<EntityUid> _orderedGrids = new();
    private readonly List<EntityUid> _gridsToRemove = new();
    private readonly List<EntityUid> _gridsToFinalize = new();
    private readonly List<(Vector2i, Tile)> _tileBatch = new();
    private readonly List<Entity<PhysicsComponent>> _throwList = new();
    private readonly HashSet<EntityUid> _throwProcessed = new();
    private readonly HashSet<EntityUid> _fragmentEntities = new();
    private readonly HashSet<EntityUid> _impactSplitGrids = new();
    // FindGridsIntersecting can replace the buffer through its ref parameter.
    private List<Entity<MapGridComponent>> _tunnelCandidates = new();
    private readonly Dictionary<EntityUid, TimeSpan> _impactedAt = new();

    private void InitializeImpact()
    {
        UpdatesAfter.Add(typeof(SharedPhysicsSystem));
        SubscribeLocalEvent<ShuttleComponent, StartCollideEvent>(OnShuttleCollide);
        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnPhysicsBeforeSolve);

        _dmgQuery = GetEntityQuery<DamageableComponent>();
        _projQuery = GetEntityQuery<ProjectileComponent>();

        Subs.CVar(_cfg, CCVars.ImpactEnabled, value => _enabled = value, true);
        Subs.CVar(_cfg, CCVars.MinimumImpactInertia, value => _minimumImpactInertia = value, true);
        Subs.CVar(_cfg, CCVars.MinimumImpactVelocity, value => _minimumImpactVelocity = value, true);
        Subs.CVar(_cfg, CCVars.TileBreakEnergyMultiplier, value => _tileBreakEnergyMultiplier = value, true);
        Subs.CVar(_cfg, CCVars.ImpactDamageMultiplier, value => _damageMultiplier = value, true);
        Subs.CVar(_cfg, CCVars.ImpactStructuralDamage, value => _structuralDamage = value, true);
        Subs.CVar(_cfg, CCVars.SparkEnergy, value => _sparkEnergy = value, true);
        Subs.CVar(_cfg, CCVars.ImpactRadius, value => _impactRadius = value, true);
        Subs.CVar(_cfg, CCVars.ImpactSlowdown, value => _impactSlowdown = value, true);
        Subs.CVar(_cfg, CCVars.ImpactMinThrowVelocity, value => _minThrowVelocity = value, true);
        Subs.CVar(_cfg, CCVars.ImpactInertiaScaling, value => _inertiaScaling = value, true);
        Subs.CVar(_cfg, CCVars.ImpactMaxTilesPerTick, value => _maxTilesPerTick = Math.Max(1, value), true);
        Subs.CVar(_cfg, CCVars.ImpactMaxGridBatchesPerTick, value => _maxGridBatchesPerTick = Math.Max(1, value), true);
        Subs.CVar(_cfg, CCVars.ImpactMaxVisualEffects, value => _maxVisualEffects = Math.Max(0, value), true);
        Subs.CVar(_cfg, CCVars.ImpactContactMergeDistance, value => _contactMergeDistance = Math.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.ImpactThrowRadiusPadding, value => _throwRadiusPadding = Math.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.ImpactFragmentCleanupTiles, value => _fragmentCleanupTiles = Math.Max(0, value), true);
        Subs.CVar(_cfg, CCVars.ImpactFragmentCleanupAcceleration, value => _fragmentCleanupAcceleration = Math.Max(0.01f, value), true);
        Subs.CVar(_cfg, CCVars.ImpactFragmentMinimumLifetime, value => _fragmentMinimumLifetime = Math.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.ImpactTunnelMinSpeed, value => _tunnelMinSpeed = Math.Max(0f, value), true);

        Subs.CVar(_cfg, MonoCVars.ImpactSweepAggression, value => _sweepAggression = value, true);
        Subs.CVar(_cfg, MonoCVars.ImpactSweepDelay, value => _sweepDelay = value, true);
        Subs.CVar(_cfg, MonoCVars.ImpactSweepRadius, value => _sweepRadius = value, true);

        _platingMass = _protoManager.Index(_platingId).Mass;
    }

    /// <summary>
    /// Conservative server-side guard for the rare case where a grid moves farther than its hull thickness in one physics substep.
    /// It intentionally does not replace physics collision or create impact damage: it only prevents a high-speed grid from crossing
    /// a neighbouring shuttle before the normal contact pipeline can see it.
    /// </summary>
    private void OnPhysicsBeforeSolve(ref PhysicsUpdateBeforeSolveEvent args)
    {
        if (args.Prediction || _tunnelMinSpeed <= 0f)
            return;

        var query = EntityQueryEnumerator<ShuttleComponent, MapGridComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var body, out var xform))
        {
            if (body.BodyType != BodyType.Dynamic || body.LinearVelocity.LengthSquared() < _tunnelMinSpeed * _tunnelMinSpeed)
                continue;

            var currentBounds = _physics.GetWorldAABB(uid, body: body, xform: xform);
            var predictedBounds = currentBounds.Translated(body.LinearVelocity * args.DeltaTime);
            var sweptBounds = currentBounds.Union(predictedBounds);

            _tunnelCandidates.Clear();
            _mapManager.FindGridsIntersecting(xform.MapID, sweptBounds, ref _tunnelCandidates, approx: true, includeMap: false);
            foreach (var candidate in _tunnelCandidates)
            {
                if (candidate.Owner == uid || !HasComp<ShuttleComponent>(candidate.Owner) ||
                    !_physicsQuery.TryComp(candidate.Owner, out var candidateBody) ||
                    !_xformQuery.TryComp(candidate.Owner, out var candidateXform) ||
                    _dockSystem.AreGridsDocked(uid, candidate.Owner) ||
                    _koronusSafety.ShouldBlockPlayerShipCollision(uid, candidate.Owner))
                {
                    continue;
                }

                var candidateBounds = _physics.GetWorldAABB(candidate.Owner, body: candidateBody, xform: candidateXform);
                if (currentBounds.Intersects(candidateBounds) || !sweptBounds.Intersects(candidateBounds))
                    continue;

                _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
                break;
            }
        }
    }

    /// <summary>
    /// Captures a grid contact. Physics emits this event for both participants, but a shuttle impact already affects both grids.
    /// Resolution is deferred until after physics and performed once for a canonical pair.
    /// </summary>
    private void OnShuttleCollide(EntityUid uid, ShuttleComponent component, ref StartCollideEvent args)
    {
        if (args.OurEntity.Id > args.OtherEntity.Id)
            return;

        if (TerminatingOrDeleted(args.OurEntity) || EntityManager.IsQueuedForDeletion(args.OurEntity) ||
            TerminatingOrDeleted(args.OtherEntity) || EntityManager.IsQueuedForDeletion(args.OtherEntity) ||
            !_gridQuery.TryComp(args.OurEntity, out var ourGrid) ||
            !_gridQuery.TryComp(args.OtherEntity, out var otherGrid))
        {
            return;
        }

        var ourXform = Transform(args.OurEntity);
        var otherXform = Transform(args.OtherEntity);
        if (ourXform.MapUid == null)
            return;

        var pair = new ImpactPairKey(args.OurEntity, args.OtherEntity);
        foreach (var worldPoint in args.WorldPoints)
        {
            var ourPoint = _transform.ToCoordinates((args.OurEntity, ourXform), new MapCoordinates(worldPoint, ourXform.MapID));
            var otherPoint = _transform.ToCoordinates((args.OtherEntity, otherXform), new MapCoordinates(worldPoint, otherXform.MapID));
            var ourVelocity = _physics.GetLinearVelocity(args.OurEntity, ourPoint.Position, args.OurBody, ourXform);
            var otherVelocity = _physics.GetLinearVelocity(args.OtherEntity, otherPoint.Position, args.OtherBody, otherXform);
            var relativeVelocity = ourVelocity - otherVelocity;
            var relativeSpeed = relativeVelocity.Length();

            if (relativeSpeed <= float.Epsilon || float.IsNaN(relativeSpeed))
                continue;

            var normalSpeed = relativeSpeed * MathF.Abs(Vector2.Dot(relativeVelocity / relativeSpeed, args.WorldNormal.Normalized()));
            var totalMass = args.OurBody.FixturesMass + args.OtherBody.FixturesMass;
            if (totalMass <= float.Epsilon)
                continue;

            var effectiveInertiaMultiplier = args.OurBody.FixturesMass * args.OtherBody.FixturesMass / totalMass;
            var effectiveInertia = normalSpeed * effectiveInertiaMultiplier;
            if ((normalSpeed < _minimumImpactVelocity && effectiveInertia < _minimumImpactInertia) || float.IsNaN(effectiveInertia))
                continue;

            var candidate = new ImpactCandidate(
                args.OurEntity,
                args.OtherEntity,
                ourXform.MapUid.Value,
                worldPoint,
                new Vector2i((int)MathF.Floor(ourPoint.X / ourGrid.TileSize), (int)MathF.Floor(ourPoint.Y / ourGrid.TileSize)),
                new Vector2i((int)MathF.Floor(otherPoint.X / otherGrid.TileSize), (int)MathF.Floor(otherPoint.Y / otherGrid.TileSize)),
                ourVelocity,
                otherVelocity,
                normalSpeed,
                effectiveInertia,
                effectiveInertiaMultiplier,
                args.OurFixture.Density,
                args.OtherFixture.Density);

            QueueImpactCandidate(pair, candidate);
        }
    }

    private void QueueImpactCandidate(ImpactPairKey pair, ImpactCandidate candidate)
    {
        if (!_contactClusters.TryGetValue(pair, out var clusters))
        {
            clusters = new List<ImpactCandidate>();
            _contactClusters.Add(pair, clusters);
        }

        var mergeDistanceSquared = _contactMergeDistance * _contactMergeDistance;
        foreach (var cluster in clusters)
        {
            if (Vector2.DistanceSquared(cluster.WorldPoint, candidate.WorldPoint) > mergeDistanceSquared)
                continue;

            if (candidate.EffectiveInertia > cluster.EffectiveInertia)
                cluster.ReplaceWith(candidate);

            return;
        }

        clusters.Add(candidate);
    }

    private void UpdateImpact()
    {
        ResolveImpactCandidates();
        CommitQueuedDestruction();
    }

    private void ResolveImpactCandidates()
    {
        if (_contactClusters.Count == 0)
            return;

        _orderedPairs.Clear();
        _orderedPairs.AddRange(_contactClusters.Keys);
        _orderedPairs.Sort(static (first, second) =>
        {
            var firstCompare = first.First.Id.CompareTo(second.First.Id);
            return firstCompare != 0 ? firstCompare : first.Second.Id.CompareTo(second.Second.Id);
        });

        foreach (var pair in _orderedPairs)
        {
            var clusters = _contactClusters[pair];
            clusters.Sort(static (first, second) => first.WorldPoint.X != second.WorldPoint.X
                ? first.WorldPoint.X.CompareTo(second.WorldPoint.X)
                : first.WorldPoint.Y.CompareTo(second.WorldPoint.Y));

            foreach (var impact in clusters)
                ResolveImpact(impact);
        }

        _contactClusters.Clear();
    }

    private void ResolveImpact(ImpactCandidate impact)
    {
        if (TerminatingOrDeleted(impact.OurGrid) || TerminatingOrDeleted(impact.OtherGrid) ||
            !_gridQuery.TryComp(impact.OurGrid, out var ourGrid) ||
            !_gridQuery.TryComp(impact.OtherGrid, out var otherGrid) ||
            !_physicsQuery.TryComp(impact.OurGrid, out var ourBody) ||
            !_physicsQuery.TryComp(impact.OtherGrid, out var otherBody))
        {
            return;
        }

        var ourXform = Transform(impact.OurGrid);
        var otherXform = Transform(impact.OtherGrid);
        PlayImpactSound(impact);

        if (!_enabled ||
            HasComp<GridGodModeComponent>(impact.OurGrid) || HasComp<ForceAnchorComponent>(impact.OurGrid) ||
            HasComp<GridGodModeComponent>(impact.OtherGrid) || HasComp<ForceAnchorComponent>(impact.OtherGrid) ||
            _dockSystem.AreGridsDocked(impact.OurGrid, impact.OtherGrid) ||
            _koronusSafety.ShouldBlockPlayerShipCollision(impact.OurGrid, impact.OtherGrid))
        {
            return;
        }

        var ourData = BuildImpactGridData(impact.OurGrid, ourGrid, impact.OurTile);
        var otherData = BuildImpactGridData(impact.OtherGrid, otherGrid, impact.OtherTile);
        if (ourData.TileCount == 0 || otherData.TileCount == 0)
            return;

        var energyMultiplier = MathF.Pow(impact.NormalSpeed, 2) / 2f;
        var ourMassDamageReduction = MathF.Max(otherData.Mass / ourData.Mass, 1f);
        var otherMassDamageReduction = MathF.Max(ourData.Mass / otherData.Mass, 1f);
        var inertiaMultiplier = MathF.Pow(impact.EffectiveInertiaMultiplier / BaseShuttleMass, _inertiaScaling);
        var toUsEnergy = otherData.Mass * energyMultiplier * inertiaMultiplier * ourMassDamageReduction;
        var toOtherEnergy = ourData.Mass * energyMultiplier * inertiaMultiplier * otherMassDamageReduction;

        var shieldFactor = GetShieldCollisionFactor(impact.OurGrid) * GetShieldCollisionFactor(impact.OtherGrid);
        toUsEnergy *= shieldFactor;
        toOtherEnergy *= shieldFactor;

        var logImpact = LogImpact.High;
        if (toUsEnergy + toOtherEnergy > 2f * _tileBreakEnergyMultiplier * _platingMass)
        {
            logImpact = LogImpact.Extreme;
            var sweepPoint = new EntityCoordinates(impact.MapUid, impact.WorldPoint);
            _sweep.QueueSweep(sweepPoint, TimeSpan.FromSeconds(_sweepDelay), _sweepRadius, _sweepAggression);
        }

        if (CheckShouldLog(impact.OurGrid) && CheckShouldLog(impact.OtherGrid))
        {
            _logger.Add(LogType.ShuttleImpact, logImpact,
                $"Shuttle impact of {ToPrettyString(impact.OurGrid)} with {ToPrettyString(impact.OtherGrid)} at {impact.WorldPoint}; " +
                $"our mass: {ourData.Mass}, other: {otherData.Mass}, velocity {impact.NormalSpeed}");
        }

        _impactedAt[impact.OurGrid] = _gameTiming.CurTime;
        _impactedAt[impact.OtherGrid] = _gameTiming.CurTime;

        var inelasticVelocity = (impact.OurVelocity * ourData.Mass + impact.OtherVelocity * otherData.Mass) /
                                (ourData.Mass + otherData.Mass);

        DoGridImpact((impact.OurGrid, ourGrid, ourXform, ourBody), impact.OurFixtureDensity,
            inelasticVelocity, impact.OurVelocity, ourData, toUsEnergy);
        DoGridImpact((impact.OtherGrid, otherGrid, otherXform, otherBody), impact.OtherFixtureDensity,
            inelasticVelocity, impact.OtherVelocity, otherData, toOtherEnergy);
    }

    private float GetShieldCollisionFactor(EntityUid grid)
    {
        return TryComp<ShipShieldedComponent>(grid, out var shielded) &&
               TryComp<ShipShieldEmitterComponent>(shielded.Source, out var emitter)
            ? emitter.CollisionResistanceMultiplier
            : 1f;
    }

    private void PlayImpactSound(ImpactCandidate impact)
    {
        var volume = MathF.Min(10f, MathF.Pow(impact.NormalSpeed, 0.5f) - 5f);
        var parameters = AudioParams.Default.WithVariation(SharedContentAudioSystem.DefaultVariation).WithVolume(volume);
        _audio.PlayPvs(_shuttleImpactSound, new EntityCoordinates(impact.MapUid, impact.WorldPoint), parameters);
    }

    private ImpactGridData BuildImpactGridData(EntityUid uid, MapGridComponent grid, Vector2i centerTile)
    {
        var data = new ImpactGridData(centerTile);
        foreach (var tileRef in _mapSystem.GetLocalTilesIntersecting(uid, grid, new Circle(centerTile, _impactRadius)))
        {
            var distance = Vector2.Distance(centerTile, tileRef.GridIndices);
            var tileData = new ImpactTileData(tileRef.GridIndices, distance, _turf.GetContentTileDefinition(tileRef).Mass);
            data.Tiles.Add(tileData.Tile, tileData);
            data.Mass += tileData.Mass;
        }

        if (data.Tiles.Count == 0)
            return data;

        var tileSize = grid.TileSize;
        var minimum = new Vector2(centerTile.X * tileSize, centerTile.Y * tileSize);
        var maximum = minimum + new Vector2(tileSize, tileSize);
        var area = new Box2(minimum, maximum).Enlarged(_impactRadius * tileSize);
        _lookup.GetLocalEntitiesIntersecting(uid, area, data.Entities, LookupFlags.All);

        foreach (var entity in data.Entities)
        {
            if (entity == uid || !_physicsQuery.TryComp(entity, out var physics) ||
                !_xformQuery.TryComp(entity, out var xform) || xform.GridUid != uid)
            {
                continue;
            }

            var tile = GetTileAtPosition(xform.Coordinates.Position, tileSize);
            if (data.Tiles.ContainsKey(tile))
                data.Mass += physics.FixturesMass;
        }

        return data;
    }

    private void DoGridImpact(
        Entity<MapGridComponent, TransformComponent, PhysicsComponent> entity,
        float fixtureDensity,
        Vector2 inelasticVelocity,
        Vector2 velocity,
        ImpactGridData data,
        float energy)
    {
        var (_, grid, _, body) = entity;
        var radius = Math.Min(_impactRadius, MathF.Sqrt(energy / _tileBreakEnergyMultiplier / _platingMass));
        var slowdown = MathF.Min(1f, _impactSlowdown * data.TileCount * fixtureDensity / body.FixturesMass);
        var postImpactVelocity = Vector2.Lerp(velocity, inelasticVelocity, slowdown);
        var deltaVelocity = -velocity + postImpactVelocity;
        _physics.ApplyLinearImpulse(entity, deltaVelocity * body.FixturesMass, body: body);

        ProcessImpactZone(entity.Owner, grid, data, energy, deltaVelocity.Normalized(), radius);

        if (deltaVelocity.Length() > MinImpulseVelocity)
            ThrowEntitiesOnGrid(entity.Owner, -deltaVelocity, data.CenterTile, radius);
    }

    private void ProcessImpactZone(
        EntityUid gridUid,
        MapGridComponent grid,
        ImpactGridData data,
        float energy,
        Vector2 throwDirection,
        float radius)
    {
        var blockedTiles = new HashSet<Vector2i>();
        var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = 0, ["Structural"] = 0 } };
        var halfTile = grid.TileSize / 2f;

        foreach (var entity in data.Entities)
        {
            if (!_xformQuery.TryComp(entity, out var xform) || xform.GridUid != gridUid)
                continue;

            var entityTile = GetTileAtPosition(xform.Coordinates.Position, grid.TileSize);
            if (!data.Tiles.TryGetValue(entityTile, out var tileData) || tileData.Distance > radius)
                continue;

            var tileCenter = (new Vector2(entityTile.X, entityTile.Y) + new Vector2(0.5f, 0.5f)) * grid.TileSize;
            var toCenter = tileCenter - xform.Coordinates.Position;
            if (MathF.Abs(toCenter.X) > halfTile || MathF.Abs(toCenter.Y) > halfTile)
                continue;

            var distanceFactor = 1f - tileData.Distance / (radius + 1f);
            var scaledDamage = energy * distanceFactor * _damageMultiplier;
            if (_dmgQuery.TryComp(entity, out var damageable))
            {
                damage.DamageDict["Blunt"] = scaledDamage;
                damage.DamageDict["Structural"] = scaledDamage * _structuralDamage;
                _damageSys.TryChangeDamage(entity, damage, damageable: damageable);
            }

            if (TerminatingOrDeleted(entity) || EntityManager.IsQueuedForDeletion(entity) ||
                !_physicsQuery.TryComp(entity, out var physics))
            {
                continue;
            }

            if ((physics.BodyType & BodyType.Static) != 0 &&
                (physics.CollisionLayer & (int)CollisionGroup.Impassable) != 0)
            {
                blockedTiles.Add(entityTile);
                continue;
            }

            var direction = throwDirection * distanceFactor;
            _throwing.TryThrow(entity, direction, physics, xform, _projQuery, direction.Length(), playSound: false);
        }

        var sparks = _maxVisualEffects > 0 ? new List<NetCoordinates>(_maxVisualEffects) : null;
        foreach (var tileData in data.Tiles.Values)
        {
            if (tileData.Distance > radius || blockedTiles.Contains(tileData.Tile))
                continue;

            var distanceFactor = 1f - tileData.Distance / (radius + 1f);
            var tileEnergy = energy * distanceFactor;
            if (tileEnergy > tileData.Mass * _tileBreakEnergyMultiplier)
                QueueBrokenTile(gridUid, tileData.Tile);

            if (sparks != null && sparks.Count < _maxVisualEffects && tileEnergy > _sparkEnergy &&
                distanceFactor > 0.7f && _random.Prob(SparkChance))
            {
                sparks.Add(GetNetCoordinates(_mapSystem.GridTileToLocal(gridUid, grid, tileData.Tile)));
            }
        }

        if (sparks is { Count: > 0 })
            RaiseNetworkEvent(new ShuttleImpactEffectsEvent(sparks), Filter.Pvs(gridUid));
    }

    private void QueueBrokenTile(EntityUid gridUid, Vector2i tile)
    {
        if (!_pendingDestruction.TryGetValue(gridUid, out var pending))
        {
            pending = new PendingGridDestruction();
            _pendingDestruction.Add(gridUid, pending);

            // Tile collision is regenerated immediately, but splitting is delayed until every queued tile for
            // this impact batch has been committed. This avoids repeatedly splitting the same hull.
            if (_gridQuery.TryComp(gridUid, out var grid) && grid.CanSplit)
            {
                grid.CanSplit = false;
                pending.SplitDeferred = true;
            }
        }

        if (!pending.KnownTiles.Add(tile))
            return;

        pending.Tiles.Enqueue(tile);
    }

    private void CommitQueuedDestruction()
    {
        if (_pendingDestruction.Count == 0)
            return;

        CommitQueuedDestructionInternal();
    }

    private void CommitQueuedDestructionInternal()
    {

        _orderedGrids.Clear();
        _orderedGrids.AddRange(_pendingDestruction.Keys);
        _orderedGrids.Sort(static (first, second) => first.Id.CompareTo(second.Id));

        var tileBudget = _maxTilesPerTick;
        var gridBudget = _maxGridBatchesPerTick;
        _gridsToRemove.Clear();
        _gridsToFinalize.Clear();

        foreach (var gridUid in _orderedGrids)
        {
            if (tileBudget <= 0 || gridBudget <= 0)
                break;

            if (TerminatingOrDeleted(gridUid) || !_gridQuery.TryComp(gridUid, out var grid))
            {
                _gridsToRemove.Add(gridUid);
                continue;
            }

            var pending = _pendingDestruction[gridUid];
            _tileBatch.Clear();
            var batchSize = Math.Min(tileBudget, pending.Tiles.Count);
            for (var i = 0; i < batchSize; i++)
            {
                var tile = pending.Tiles.Dequeue();
                pending.KnownTiles.Remove(tile);
                _tileBatch.Add((tile, Tile.Empty));
            }

            if (_tileBatch.Count == 0)
            {
                _gridsToRemove.Add(gridUid);
                continue;
            }

            _mapSystem.SetTiles(gridUid, grid, _tileBatch);
            var committed = _tileBatch.Count;
            tileBudget -= committed;
            gridBudget--;

            if (pending.Tiles.Count == 0)
                _gridsToFinalize.Add(gridUid);
        }

        foreach (var gridUid in _gridsToFinalize)
        {
            if (!_pendingDestruction.TryGetValue(gridUid, out var pending))
                continue;

            if (pending.SplitDeferred && _gridQuery.TryComp(gridUid, out var grid) && !TerminatingOrDeleted(gridUid))
            {
                grid.CanSplit = true;
                _impactSplitGrids.Add(gridUid);
                try
                {
                    _gridFixtures.CheckSplits(gridUid);
                }
                finally
                {
                    _impactSplitGrids.Remove(gridUid);
                }

            }

            _gridsToRemove.Add(gridUid);
        }

        foreach (var gridUid in _gridsToRemove)
            _pendingDestruction.Remove(gridUid);
    }

    private void OnImpactGridSplit(ref GridSplitEvent args)
    {
        if (!_impactSplitGrids.Contains(args.Grid))
            return;

        foreach (var gridUid in args.NewGrids)
        {
            if (_fragmentCleanupTiles <= 0 ||
                !_gridQuery.TryComp(gridUid, out var grid) ||
                _mapSystem.GetFilledTileCount((gridUid, grid)) > _fragmentCleanupTiles ||
                !IsEmptyImpactFragment(gridUid, grid))
            {
                continue;
            }

            var aggressiveTiles = Math.Max(1, _cfg.GetCVar(MonoCVars.GridCleanupAggressiveTiles));
            var scale = Math.Clamp(_mapSystem.GetFilledTileCount((gridUid, grid)) / (float) aggressiveTiles, 0.1f, 1f);
            var cleanupDuration = _cfg.GetCVar(MonoCVars.GridCleanupDuration);
            var maximumAcceleration = _fragmentMinimumLifetime <= 0f
                ? _fragmentCleanupAcceleration
                : cleanupDuration * scale / _fragmentMinimumLifetime;
            var state = EnsureComp<GridCleanupGridComponent>(gridUid);
            // A split can inherit a cleanup component from an old grid. Do not let an accumulated timer erase a
            // freshly-created fragment before its guaranteed lifetime has elapsed.
            state.CleanupAccumulator = TimeSpan.Zero;
            state.LastEvaluation = TimeSpan.Zero;
            state.EligibilityActive = false;
            state.CleanupAcceleration = Math.Min(_fragmentCleanupAcceleration, maximumAcceleration);
        }
    }

    private bool IsEmptyImpactFragment(EntityUid gridUid, MapGridComponent grid)
    {
        _fragmentEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, grid.LocalAABB, _fragmentEntities, LookupFlags.All);
        foreach (var entity in _fragmentEntities)
        {
            if (entity != gridUid && _xformQuery.TryComp(entity, out var xform) && xform.GridUid == gridUid)
                return false;
        }

        return true;
    }

    private void ThrowEntitiesOnGrid(EntityUid gridUid, Vector2 direction, Vector2i centerTile, float impactRadius)
    {
        if (direction.LengthSquared() <= _minThrowVelocity * _minThrowVelocity ||
            !TryComp<BroadphaseComponent>(gridUid, out var lookup) ||
            !_gridQuery.TryComp(gridUid, out var grid))
        {
            return;
        }

        _throwList.Clear();
        _throwProcessed.Clear();
        var state = (_throwList, _throwProcessed, _physicsQuery);
        var radius = MathF.Max(0f, impactRadius) + _throwRadiusPadding;
        var center = (new Vector2(centerTile.X, centerTile.Y) + new Vector2(0.5f, 0.5f)) * grid.TileSize;
        var queryBounds = new Box2(center - new Vector2(radius, radius), center + new Vector2(radius, radius));
        lookup.DynamicTree.QueryAabb(ref state, GridQueryCallback, queryBounds, true);

        var knockdownTime = TimeSpan.FromSeconds(5);
        foreach (var entity in _throwList)
        {
            if (_buckle.IsBuckled(entity, _buckleQuery.CompOrNull(entity)) ||
                _movedByPressureQuery.TryComp(entity, out var movedByPressure) && !movedByPressure.Enabled)
            {
                continue;
            }

            _stuns.TryKnockdown(entity.Owner, knockdownTime, true);
            _throwing.TryThrow(entity, direction, entity.Comp, Transform(entity), _projQuery, direction.Length(), playSound: false);
        }
    }

    private static bool GridQueryCallback(
        ref (List<Entity<PhysicsComponent>> List, HashSet<EntityUid> Processed, EntityQuery<PhysicsComponent> PhysicsQuery) state,
        in EntityUid uid)
    {
        if (state.Processed.Add(uid) && state.PhysicsQuery.TryComp(uid, out var body))
            state.List.Add((uid, body));

        return true;
    }

    private static bool GridQueryCallback(
        ref (List<Entity<PhysicsComponent>> List, HashSet<EntityUid> Processed, EntityQuery<PhysicsComponent> PhysicsQuery) state,
        in FixtureProxy proxy)
    {
        var owner = proxy.Entity;
        return GridQueryCallback(ref state, in owner);
    }

    private static Vector2i GetTileAtPosition(Vector2 position, ushort tileSize)
    {
        return new Vector2i((int)MathF.Floor(position.X / tileSize), (int)MathF.Floor(position.Y / tileSize));
    }

    private bool CheckShouldLog(EntityUid uid)
    {
        return !_impactedAt.TryGetValue(uid, out var impactedAt) ||
               _gameTiming.CurTime >= impactedAt + _adminLogSpacing;
    }

    private readonly record struct ImpactPairKey(EntityUid First, EntityUid Second);

    private sealed class ImpactCandidate
    {
        public EntityUid OurGrid;
        public EntityUid OtherGrid;
        public EntityUid MapUid;
        public Vector2 WorldPoint;
        public Vector2i OurTile;
        public Vector2i OtherTile;
        public Vector2 OurVelocity;
        public Vector2 OtherVelocity;
        public float NormalSpeed;
        public float EffectiveInertia;
        public float EffectiveInertiaMultiplier;
        public float OurFixtureDensity;
        public float OtherFixtureDensity;
        public ImpactCandidate(EntityUid ourGrid, EntityUid otherGrid, EntityUid mapUid, Vector2 worldPoint,
            Vector2i ourTile, Vector2i otherTile, Vector2 ourVelocity, Vector2 otherVelocity, float normalSpeed,
            float effectiveInertia, float effectiveInertiaMultiplier, float ourFixtureDensity, float otherFixtureDensity)
        {
            OurGrid = ourGrid;
            OtherGrid = otherGrid;
            MapUid = mapUid;
            WorldPoint = worldPoint;
            OurTile = ourTile;
            OtherTile = otherTile;
            OurVelocity = ourVelocity;
            OtherVelocity = otherVelocity;
            NormalSpeed = normalSpeed;
            EffectiveInertia = effectiveInertia;
            EffectiveInertiaMultiplier = effectiveInertiaMultiplier;
            OurFixtureDensity = ourFixtureDensity;
            OtherFixtureDensity = otherFixtureDensity;
        }

        public void ReplaceWith(ImpactCandidate other)
        {
            OurGrid = other.OurGrid;
            OtherGrid = other.OtherGrid;
            MapUid = other.MapUid;
            WorldPoint = other.WorldPoint;
            OurTile = other.OurTile;
            OtherTile = other.OtherTile;
            OurVelocity = other.OurVelocity;
            OtherVelocity = other.OtherVelocity;
            NormalSpeed = other.NormalSpeed;
            EffectiveInertia = other.EffectiveInertia;
            EffectiveInertiaMultiplier = other.EffectiveInertiaMultiplier;
            OurFixtureDensity = other.OurFixtureDensity;
            OtherFixtureDensity = other.OtherFixtureDensity;
        }
    }

    private readonly record struct ImpactTileData(Vector2i Tile, float Distance, float Mass);

    private sealed class ImpactGridData
    {
        public readonly Dictionary<Vector2i, ImpactTileData> Tiles = new();
        public readonly HashSet<EntityUid> Entities = new();
        public readonly Vector2i CenterTile;
        public float Mass;
        public int TileCount => Tiles.Count;

        public ImpactGridData(Vector2i centerTile)
        {
            CenterTile = centerTile;
        }
    }

    private sealed class PendingGridDestruction
    {
        public readonly Queue<Vector2i> Tiles = new();
        public readonly HashSet<Vector2i> KnownTiles = new();
        public bool SplitDeferred;
    }
}
