using System.Numerics;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Enforces the authored system radius independently of physics collisions, including for ghosts.
/// </summary>
public sealed class KoronusSectorBoundarySystem : EntitySystem
{
    private const float GridSafetyMargin = 5f;
    private const float EmergencyDeletionMargin = 1f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private KoronusSectorRuleSystem _sector = default!;
    [Dependency] private MapSystem _maps = default!;

    private readonly Dictionary<MapId, SystemBoundary> _boundaries = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastWarning = new();
    private readonly Dictionary<EntityUid, CleanupCandidate> _cleanupCandidates = new();
    private readonly HashSet<MapId> _observedMaps = new();
    private readonly List<EntityUid> _warningRemovals = new();
    private readonly List<EntityUid> _cleanupCandidateRemovals = new();

    private TimeSpan _nextUpdate;
    private TimeSpan _nextWarningPrune;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan WarningPruneInterval = TimeSpan.FromMinutes(1);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;
        BuildBoundaryIndex();

        if (_boundaries.Count == 0)
            return;

        BuildObservedMapIndex();
        ClampBoundaryRoots();
        WarnPlayersNearBoundary();
        PruneWarningHistory();
        CleanupStillOutOfBoundsEntities();
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public (int WarningHistory, int CleanupCandidates) GetLongRunStatus()
    {
        return (_lastWarning.Count, _cleanupCandidates.Count);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _boundaries.Clear();
        _lastWarning.Clear();
        _cleanupCandidates.Clear();
        _observedMaps.Clear();
        _warningRemovals.Clear();
        _cleanupCandidateRemovals.Clear();
        _nextUpdate = default;
        _nextWarningPrune = default;
    }

    private void BuildBoundaryIndex()
    {
        _boundaries.Clear();

        var query = EntityQueryEnumerator<KoronusSystemMapComponent, KoronusSystemBoundaryComponent, MapComponent>();
        while (query.MoveNext(out var mapUid, out var runtime, out var boundary, out var map))
        {
            if (map.MapId != MapId.Nullspace && boundary.Radius > 0f)
                _boundaries[map.MapId] = new SystemBoundary(mapUid, map.MapId, runtime, boundary);
        }
    }

    /// <summary>
    /// A system map owns its grids and gridless roots directly. Walking only those children avoids
    /// two global entity queries every 200 ms and deliberately ignores everything carried by a grid.
    /// </summary>
    private void ClampBoundaryRoots()
    {
        foreach (var boundary in _boundaries.Values)
        {
            if (!ShouldProcessMap(boundary.MapId) ||
                !TryComp<TransformComponent>(boundary.MapUid, out var mapTransform))
            {
                continue;
            }

            var children = mapTransform.ChildEnumerator;
            while (children.MoveNext(out var uid))
            {
                if (!TryComp<TransformComponent>(uid, out var transform))
                    continue;

                if (HasComp<MapGridComponent>(uid))
                    ClampGrid(uid, transform, boundary);
                else if (transform.GridUid == null && !HasComp<MapComponent>(uid))
                    ClampMapEntity(uid, transform, boundary);
            }
        }
    }

    private void ClampGrid(EntityUid uid, TransformComponent transform, SystemBoundary boundary)
    {
        var worldAabb = _physics.GetWorldAABB(uid, xform: transform);
        var center = worldAabb.Center;
        var halfDiagonal = (worldAabb.TopRight - center).Length();
        var safeRadius = boundary.Component.Radius - MathF.Max(GridSafetyMargin, halfDiagonal);

        if (safeRadius <= 0f)
        {
            Log.Warning($"Koronus grid {ToPrettyString(uid)} is larger than the safe radius of {boundary.Runtime.SystemId}.");
            return;
        }

        var offset = center - boundary.Component.Origin;
        if (offset.LengthSquared() <= safeRadius * safeRadius)
            return;

        var targetCenter = boundary.Component.Origin + offset.Normalized() * safeRadius;
        var targetPosition = _transform.GetWorldPosition(transform) + targetCenter - center;
        _transform.SetWorldPosition((uid, transform), targetPosition);
        StopPhysics(uid);
    }

    private void ClampMapEntity(EntityUid uid, TransformComponent transform, SystemBoundary boundary)
    {
        var worldPosition = _transform.GetWorldPosition(transform);
        var offset = worldPosition - boundary.Component.Origin;
        var radius = boundary.Component.Radius;
        if (offset.LengthSquared() <= radius * radius)
            return;

        TrackCleanupCandidate(uid, boundary.MapId, boundary.Component.CleanupDelay, offset.Length() > radius + EmergencyDeletionMargin);

        // Gridless roots (primarily ghosts) use the same non-colliding correction as shuttles.
        var targetPosition = boundary.Component.Origin + offset.Normalized() * (radius - GridSafetyMargin);
        _transform.SetWorldPosition((uid, transform), targetPosition);
        StopPhysics(uid);
    }

    private void WarnPlayersNearBoundary()
    {
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } entity ||
                !TryComp<TransformComponent>(entity, out var transform) ||
                !_boundaries.TryGetValue(transform.MapID, out var boundary))
            {
                continue;
            }

            var offset = _transform.GetWorldPosition(transform) - boundary.Component.Origin;
            var warningRadius = boundary.Component.Radius * boundary.Component.WarningFraction;
            if (offset.LengthSquared() < warningRadius * warningRadius)
                continue;

            if (_lastWarning.TryGetValue(entity, out var lastWarning) &&
                _timing.CurTime - lastWarning < TimeSpan.FromSeconds(boundary.Component.WarningAnnouncementCooldown))
            {
                continue;
            }

            _lastWarning[entity] = _timing.CurTime;
            var systemName = _sector.TryGetSystemPrototype(boundary.Runtime.SystemId, out var system)
                ? system.DisplayName
                : boundary.Runtime.SystemId;
            var message = Loc.GetString("koronus-boundary-warning", ("system", systemName));
            _chat.DispatchFilteredAnnouncement(
                Filter.SinglePlayer(session),
                message,
                sender: Loc.GetString("koronus-boundary-announcer"),
                playSound: true,
                announcementSound: new SoundPathSpecifier("/Audio/Effects/adminhelp.ogg"),
                colorOverride: Color.Red);
        }
    }

    private void BuildObservedMapIndex()
    {
        _observedMaps.Clear();

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { Valid: true } entity &&
                TryComp<TransformComponent>(entity, out var transform))
            {
                // Ordinary ghosts do not wake a sector, but their movement still needs the hard boundary.
                _observedMaps.Add(transform.MapID);
            }
        }
    }

    private bool ShouldProcessMap(MapId mapId)
    {
        return _observedMaps.Contains(mapId) || !_maps.IsPaused(mapId);
    }

    private void TrackCleanupCandidate(EntityUid uid, MapId mapId, float cleanupDelay, bool emergency)
    {
        if (!emergency || _cleanupCandidates.ContainsKey(uid))
            return;

        _cleanupCandidates[uid] = new CleanupCandidate(mapId, _timing.CurTime + TimeSpan.FromSeconds(cleanupDelay));
    }

    private void CleanupStillOutOfBoundsEntities()
    {
        _cleanupCandidateRemovals.Clear();
        foreach (var (uid, candidate) in _cleanupCandidates)
        {
            if (TerminatingOrDeleted(uid) ||
                !TryComp<TransformComponent>(uid, out var transform) ||
                !_boundaries.TryGetValue(candidate.MapId, out var boundary))
            {
                _cleanupCandidateRemovals.Add(uid);
                continue;
            }

            var outside = (_transform.GetWorldPosition(transform) - boundary.Component.Origin).LengthSquared() >
                          boundary.Component.Radius * boundary.Component.Radius;
            if (!outside)
            {
                _cleanupCandidateRemovals.Add(uid);
                continue;
            }

            if (_timing.CurTime >= candidate.DeleteAt)
            {
                QueueDel(uid);
                _cleanupCandidateRemovals.Add(uid);
            }
        }

        foreach (var uid in _cleanupCandidateRemovals)
            _cleanupCandidates.Remove(uid);
    }

    private void PruneWarningHistory()
    {
        if (_timing.CurTime < _nextWarningPrune)
            return;

        _nextWarningPrune = _timing.CurTime + WarningPruneInterval;
        var maximumCooldown = TimeSpan.Zero;
        foreach (var boundary in _boundaries.Values)
        {
            maximumCooldown = TimeSpan.FromSeconds(Math.Max(
                maximumCooldown.TotalSeconds,
                boundary.Component.WarningAnnouncementCooldown));
        }

        _warningRemovals.Clear();
        foreach (var (uid, lastWarning) in _lastWarning)
        {
            if (TerminatingOrDeleted(uid) || _timing.CurTime - lastWarning >= maximumCooldown)
                _warningRemovals.Add(uid);
        }

        foreach (var uid in _warningRemovals)
            _lastWarning.Remove(uid);
    }

    private void StopPhysics(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics))
            return;

        _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(uid, 0f, body: physics);
    }

    private readonly record struct SystemBoundary(
        EntityUid MapUid,
        MapId MapId,
        KoronusSystemMapComponent Runtime,
        KoronusSystemBoundaryComponent Component);

    private readonly record struct CleanupCandidate(MapId MapId, TimeSpan DeleteAt);
}
