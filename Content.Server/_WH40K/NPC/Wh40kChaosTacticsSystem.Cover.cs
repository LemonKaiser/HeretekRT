using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.NPC;

public sealed partial class Wh40kChaosTacticsSystem
{
    [Dependency] private SharedMapSystem _coverMaps = default!;
    [Dependency] private TurfSystem _coverTurf = default!;

    private const CollisionGroup CoverMask = CollisionGroup.Impassable | CollisionGroup.InteractImpassable;
    private static readonly Vector2[] CoverOffsets =
    {
        new(-1, 0), new(0, -1), new(1, 0), new(0, 1),
        new(-1, -1), new(1, -1), new(1, 1), new(-1, 1),
        new(-2, -1), new(-1, -2), new(1, -2), new(2, -1),
        new(2, 1), new(1, 2), new(-1, 2), new(-2, 1),
        new(-2, 0), new(0, -2), new(2, 0), new(0, 2),
        new(-2, -2), new(2, -2), new(2, 2), new(-2, 2),
        new(-3, 0), new(0, -3), new(3, 0), new(0, 3),
    };
    private static readonly Vector2[] PeekOffsets = { Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY };
    private TimeSpan _coverBudgetUntil;
    private int _coverSearches;
    private const int CoverSearchRayLimit = 96;
    private int _coverSearchRays;
    private readonly Dictionary<EntityCoordinates, Wh40kChaosTacticsComponent> _coverClaims = new();
    [ViewVariables] public long CoverSearchCount;
    [ViewVariables] public long CoverCandidateCount;
    [ViewVariables] public long CoverSearchRayCount;
    [ViewVariables] public long CoverRayCount;

    private void ReturnToCover(Wh40kChaosTacticsComponent comp)
    {
        if (comp.CoverPeeking && comp.BurstEnd != TimeSpan.Zero)
        {
            comp.BurstEnd = TimeSpan.Zero;
            comp.ResumeFire = _timing.CurTime + comp.BurstPause;
        }
        comp.CoverPeeking = false;
        comp.CoverPhaseUntil = TimeSpan.Zero;
    }

    public void CancelCover(Wh40kChaosTacticsComponent comp)
    {
        if (comp.CoverPosition is { } hide && _coverClaims.GetValueOrDefault(hide) == comp)
            _coverClaims.Remove(hide);
        if (comp.PeekPosition is { } peek && _coverClaims.GetValueOrDefault(peek) == comp)
            _coverClaims.Remove(peek);
        ReturnToCover(comp);
        comp.CoverPosition = null;
        comp.PeekPosition = null;
        comp.CoverTarget = null;
        comp.CoverEntering = false;
        comp.NextCoverSearch = _timing.CurTime + TimeSpan.FromSeconds(1);
    }

    private bool CoverPointFree(EntityUid grid, MapGridComponent gridComp, EntityCoordinates point)
    {
        return _coverMaps.TryGetTileRef(grid, gridComp, point, out var tile) &&
               !_coverTurf.IsSpace(tile) && !_coverTurf.IsTileBlocked(tile, CollisionGroup.MobMask);
    }

    private bool CoverRayClear(EntityUid uid, MapCoordinates start, MapCoordinates end, float range,
        CollisionGroup mask, EntityUid? target = null, bool searching = false)
    {
        if (searching)
        {
            if (_coverSearchRays >= CoverSearchRayLimit)
                return false;
            _coverSearchRays++;
            CoverSearchRayCount++;
        }
        CoverRayCount++;
        return _interaction.InRangeUnobstructed(start, end, range, mask, e => e == uid || e == target);
    }

    private MapCoordinates GetCoverThreat(MapCoordinates origin, EntityUid target, Wh40kChaosTacticsComponent comp)
    {
        var enemy = _transform.GetMapCoordinates(target);
        if (_timing.CurTime - comp.ThreatObservedAt <= TimeSpan.FromSeconds(3) &&
            comp.ThreatPosition is { } observed && observed.IsValid(EntityManager))
        {
            var threat = _transform.ToMapCoordinates(observed);
            if (origin.InRange(threat, AlertVisionRange))
                return threat;
        }
        return enemy;
    }

    private bool CoverShieldsBody(EntityUid uid, MapCoordinates hide, MapCoordinates threat, float radius,
        float range, EntityUid? source, bool searching = false)
    {
        var delta = threat.Position - hide.Position;
        if (delta.LengthSquared() < 0.01f)
            return false;
        var edge = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * (radius + 0.05f);
        for (var i = -1; i <= 1; i++)
        {
            // An exhausted search cannot certify protection, even though a blocked ray returns false too.
            if (searching && _coverSearchRays >= CoverSearchRayLimit ||
                CoverRayClear(uid, new MapCoordinates(hide.Position + edge * i, hide.MapId), threat,
                    range, CoverMask, source, searching))
                return false;
        }
        return true;
    }

    private bool CoverSegmentClear(EntityUid uid, MapCoordinates start, MapCoordinates end, float radius, bool searching = false)
    {
        var delta = end.Position - start.Position;
        var length = delta.Length();
        if (start.MapId != end.MapId || length > 4f)
            return false;
        if (length < 0.01f)
            return true;
        if (Transform(uid).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;
        var steps = Math.Max(1, (int)MathF.Ceiling(length / 0.5f));
        for (var step = 0; step <= steps; step++)
        {
            var point = new MapCoordinates(Vector2.Lerp(start.Position, end.Position, (float)step / steps), start.MapId);
            var coordinates = _transform.WithEntityId(_transform.ToCoordinates(point), grid);
            if (!_coverMaps.TryGetTileRef(grid, gridComp, coordinates, out var tile) || _coverTurf.IsSpace(tile))
                return false;
        }
        var edge = new Vector2(-delta.Y, delta.X) / length * (radius + 0.05f);
        for (var i = -1; i <= 1; i++)
        {
            if (!CoverRayClear(uid, new MapCoordinates(start.Position + edge * i, start.MapId),
                    new MapCoordinates(end.Position + edge * i, end.MapId), length + 0.1f,
                    CollisionGroup.MobMask | CollisionGroup.InteractImpassable, searching: searching))
                return false;
        }
        return true;
    }

    private bool FindCover(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp, float radius)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;
        if (_timing.CurTime >= _coverBudgetUntil)
        {
            _coverBudgetUntil = _timing.CurTime + TimeSpan.FromSeconds(0.2);
            _coverSearches = 0;
            _coverSearchRays = 0;
        }
        if (_coverSearches >= 4 || _coverSearchRays >= CoverSearchRayLimit)
        {
            comp.NextCoverSearch = _timing.CurTime + TimeSpan.FromSeconds(0.2 + (uid.GetHashCode() & 7) * 0.03);
            return false;
        }
        _coverSearches++;
        CoverSearchCount++;
        comp.NextCoverSearch = _timing.CurTime + TimeSpan.FromSeconds(3);
        var origin = _transform.GetMapCoordinates(uid);
        var enemy = _transform.GetMapCoordinates(target);
        var threat = GetCoverThreat(origin, target, comp);
        var local = _transform.WithEntityId(xform.Coordinates, grid).Position;
        var centre = new Vector2(MathF.Floor(local.X) + 0.5f, MathF.Floor(local.Y) + 0.5f);
        // Global storage and search work stay bounded even during very large events.
        if (_coverClaims.Count >= 128)
        {
            foreach (var (point, claim) in _coverClaims)
            {
                if (_timing.CurTime >= claim.CoverExpiresAt)
                    _coverClaims.Remove(point);
            }
            if (_coverClaims.Count >= 128)
                return false;
        }
        EntityCoordinates? bestHide = null;
        EntityCoordinates? bestPeek = null;
        var bestEntering = false;
        var bestScore = float.MaxValue;
        foreach (var offset in CoverOffsets)
        {
            if (_coverSearchRays >= CoverSearchRayLimit)
                break;
            CoverCandidateCount++;
            var hide = new EntityCoordinates(grid, centre + offset);
            if (!CoverPointFree(grid, gridComp, hide) || CoverPointReserved(hide))
                continue;
            var hideMap = _transform.ToMapCoordinates(hide);
            if (Vector2.Distance(origin.Position, hideMap.Position) >= bestScore)
                continue;
            if (hideMap.InRange(enemy, 3f) || hideMap.InRange(threat, 3f) ||
                !CoverShieldsBody(uid, hideMap, threat, radius, MathF.Max(comp.RangedRange, AlertVisionRange) + 4f,
                    threat.Equals(enemy) ? target : comp.ThreatTarget, searching: true))
                continue;
            var direct = CoverSegmentClear(uid, origin, hideMap, radius, searching: true);
            foreach (var peekOffset in PeekOffsets)
            {
                if (_coverSearchRays >= CoverSearchRayLimit)
                    break;
                var peek = new EntityCoordinates(grid, hide.Position + peekOffset);
                if (!CoverPointFree(grid, gridComp, peek) || CoverPointReserved(peek))
                    continue;
                var peekMap = _transform.ToMapCoordinates(peek);
                var score = direct ? Vector2.Distance(origin.Position, hideMap.Position)
                    : Vector2.Distance(origin.Position, peekMap.Position) + 1.5f;
                if (score >= bestScore)
                    continue;
                if (!threat.Equals(enemy) && !CoverShieldsBody(uid, hideMap, enemy, radius, comp.RangedRange + 4f,
                        target, searching: true))
                    score += CoverRayClear(uid, hideMap, enemy, comp.RangedRange + 4f, CoverMask, target, searching: true)
                        ? 3.5f : 1.5f;
                if (score >= bestScore)
                    continue;
                if (!peekMap.InRange(enemy, comp.RangedRange) || peekMap.InRange(enemy, 3f) ||
                    !CoverRayClear(uid, peekMap, enemy, comp.RangedRange, CoverMask, target, searching: true) ||
                    !CoverSegmentClear(uid, hideMap, peekMap, radius, searching: true) ||
                    !direct && !CoverSegmentClear(uid, origin, peekMap, radius, searching: true))
                    continue;
                bestHide = hide;
                bestPeek = peek;
                bestEntering = !direct;
                bestScore = score;
            }
        }
        if (bestHide is not { } selectedHide || bestPeek is not { } selectedPeek)
        {
            if (_coverSearchRays >= CoverSearchRayLimit)
                comp.NextCoverSearch = _timing.CurTime + TimeSpan.FromSeconds(0.2 + (uid.GetHashCode() & 7) * 0.03);
            return false;
        }
        _coverClaims[selectedHide] = comp;
        _coverClaims[selectedPeek] = comp;
        comp.CoverPosition = selectedHide;
        comp.PeekPosition = selectedPeek;
        comp.CoverTarget = target;
        comp.CoverExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(10);
        comp.CoverProgressPosition = local;
        comp.CoverProgressAt = _timing.CurTime;
        comp.NextCoverValidation = TimeSpan.Zero;
        comp.CoverPeeking = false;
        comp.CoverEntering = bestEntering;
        comp.CoverPhaseUntil = TimeSpan.Zero;
        return true;
    }

    private bool CoverPointReserved(EntityCoordinates point)
    {
        if (!_coverClaims.TryGetValue(point, out var claim))
            return false;
        if (_timing.CurTime < claim.CoverExpiresAt)
            return true;
        _coverClaims.Remove(point);
        return false;
    }

    /// <summary>Only short, swept clear segments use cover; failed attempts fall back to ordinary combat.</summary>
    public bool TryUseCover(EntityUid uid, EntityUid target, Wh40kChaosTacticsComponent comp, float distance,
        out bool canShoot)
    {
        canShoot = false;
        TryComp<NPCSteeringComponent>(uid, out var oldSteering);
        if (distance <= 3f || comp.EscapeDirection != Vector2.Zero || !HasComp<GunComponent>(uid) ||
            HasComp<Wh40kChaosThrowerComponent>(uid) || HasComp<NPCMeleeCombatComponent>(uid) ||
            oldSteering != null && IsClearingObstacle(oldSteering) || !_blocker.CanMove(uid))
        {
            if (comp.CoverPosition != null)
                CancelCover(comp);
            return false;
        }
        var radius = oldSteering?.Radius ?? 0.35f;
        if (comp.CoverPosition == null && comp.TargetVisible && distance <= comp.RangedRange &&
            _timing.CurTime >= comp.NextCoverSearch)
            FindCover(uid, target, comp, radius);
        if (comp.CoverPosition is not { } hide || comp.PeekPosition is not { } peek)
            return false;
        if (comp.CoverTarget != target || !hide.IsValid(EntityManager) || !peek.IsValid(EntityManager) ||
            _timing.CurTime >= comp.CoverExpiresAt || Transform(uid).GridUid != hide.EntityId ||
            !CanFightTarget(uid, target, comp))
        {
            CancelCover(comp);
            return false;
        }

        // Keep a working position leased; validation and progress checks handle real changes and obstruction.
        comp.CoverExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(10);
        var regrouping = _timing.CurTime < comp.RegroupUntil;
        if (comp.CoverPeeking && (regrouping || _timing.CurTime < comp.ResumeFire))
            ReturnToCover(comp);
        var destination = comp.CoverEntering || comp.CoverPeeking ? peek : hide;
        var origin = _transform.GetMapCoordinates(uid);
        var destinationMap = _transform.ToMapCoordinates(destination);
        if (_timing.CurTime >= comp.NextCoverValidation)
        {
            comp.NextCoverValidation = _timing.CurTime + TimeSpan.FromSeconds(0.5);
            var enemy = _transform.GetMapCoordinates(target);
            var threat = GetCoverThreat(origin, target, comp);
            var gridComp = Comp<MapGridComponent>(hide.EntityId);
            if (!CoverPointFree(hide.EntityId, gridComp, hide) || !CoverPointFree(hide.EntityId, gridComp, peek) ||
                !CoverSegmentClear(uid, origin, destinationMap, radius) ||
                !CoverSegmentClear(uid, _transform.ToMapCoordinates(hide), _transform.ToMapCoordinates(peek), radius) ||
                !CoverShieldsBody(uid, _transform.ToMapCoordinates(hide), threat, radius,
                    MathF.Max(comp.RangedRange, AlertVisionRange) + 4f, threat.Equals(enemy) ? target : comp.ThreatTarget) ||
                !CoverRayClear(uid, _transform.ToMapCoordinates(peek), enemy, comp.RangedRange, CoverMask, target))
            {
                CancelCover(comp);
                return false;
            }
        }

        var local = _transform.WithEntityId(Transform(uid).Coordinates, hide.EntityId).Position;
        if (Vector2.DistanceSquared(local, comp.CoverProgressPosition) > 0.15f * 0.15f)
        {
            comp.CoverProgressPosition = local;
            comp.CoverProgressAt = _timing.CurTime;
        }
        if (!origin.InRange(destinationMap, 0.2f))
        {
            comp.CoverPhaseUntil = TimeSpan.Zero;
            if (_timing.CurTime - comp.CoverProgressAt > TimeSpan.FromSeconds(1.5) ||
                Vector2.Distance(local, hide.Position) > 5.25f)
            {
                CancelCover(comp);
                return false;
            }
        }
        else if (comp.CoverEntering)
        {
            if (CoverSegmentClear(uid, origin, _transform.ToMapCoordinates(hide), radius))
            {
                comp.CoverEntering = false;
                comp.CoverPhaseUntil = TimeSpan.Zero;
                destination = hide;
            }
        }
        else
        {
            comp.CoverProgressAt = _timing.CurTime;
            if (comp.CoverPhaseUntil == TimeSpan.Zero)
                comp.CoverPhaseUntil = _timing.CurTime + TimeSpan.FromSeconds(comp.CoverPeeking ? 2 : 1.2);
            if (!regrouping && _timing.CurTime >= comp.ResumeFire && _timing.CurTime >= comp.CoverPhaseUntil)
            {
                if (comp.CoverPeeking)
                    ReturnToCover(comp);
                else
                {
                    comp.CoverPeeking = true;
                    comp.CoverPhaseUntil = TimeSpan.Zero;
                    comp.CoverPeekStartedAt = _timing.CurTime;
                    comp.CoverFirstFireAt = TimeSpan.Zero;
                }
                destination = comp.CoverPeeking ? peek : hide;
            }
        }
        canShoot = !comp.CoverEntering && comp.CoverPeeking && comp.CoverPhaseUntil != TimeSpan.Zero && comp.TargetVisible && !regrouping;
        var steering = _steering.Register(uid, destination);
        steering.Range = 0.1f;
        steering.InRangeMaxSpeed = 0.03f;
        steering.ArriveOnLineOfSight = false;
        steering.DirectMove = true;
        steering.Status = SteeringStatus.Moving;
        return true;
    }
}
