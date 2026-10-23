using Content.Server.Xenoarchaeology.XenoArtifacts;
using Content.Server.NPC.HTN;
using Content.Shared.NPC.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Timing;
using System.Linq;
using System.Numerics;
using Content.Server._WH40K.SectorMap.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Cleans up temporary high-risk entities only on grids with the corresponding local safety rule.
/// Footfall refuges remove hostile NPCs on the next sweep; the damage policy protects players immediately.
/// </summary>
public sealed class KoronusSafetyHazardCleanupSystem : EntitySystem
{
    private static readonly TimeSpan CleanupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _artifactCleanupAt = [];
    private TimeSpan _nextScan;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextScan)
            return;

        _nextScan = _timing.CurTime + ScanInterval;
        UpdateArtifacts(_timing.CurTime);
        CleanupHostileNpcs();
    }

    private void UpdateArtifacts(TimeSpan now)
    {
        var artifacts = EntityQueryEnumerator<ArtifactComponent>();
        while (artifacts.MoveNext(out var uid, out _))
        {
            if (_safety.HasRule(uid, KoronusSafetyRule.ArtifactAutoCleanup))
                _artifactCleanupAt.TryAdd(uid, now + CleanupDelay);
        }

        foreach (var (uid, cleanupAt) in _artifactCleanupAt.ToArray())
        {
            if (TerminatingOrDeleted(uid) ||
                !HasComp<ArtifactComponent>(uid) ||
                !_safety.HasRule(uid, KoronusSafetyRule.ArtifactAutoCleanup))
            {
                _artifactCleanupAt.Remove(uid);
                continue;
            }

            if (cleanupAt > now)
                continue;

            QueueDel(uid);
            _artifactCleanupAt.Remove(uid);
        }
    }

    /// <summary>Also runs at bootstrap, before paused stations can admit their first visitors.</summary>
    public void CleanupHostileNpcs()
    {
        var npcs = AllEntityQuery<NpcFactionMemberComponent>();
        while (npcs.MoveNext(out var uid, out var faction))
        {
            Entity<NpcFactionMemberComponent?> factionEntity = new(uid, faction);
            if ((HasComp<MobStateComponent>(uid) || HasComp<HTNComponent>(uid)) &&
                !_safety.IsPlayerCharacter(uid) &&
                _safety.HasRule(uid, KoronusSafetyRule.HostileNpcAutoCleanup) &&
                _npcFaction.IsFactionHostile("NanoTrasen", factionEntity) &&
                !IsAllowedSurfaceWildlife(uid))
            {
                QueueDel(uid);
            }
        }
    }

    private bool IsAllowedSurfaceWildlife(EntityUid uid)
    {
        if (!TryComp<KoronusSurfaceWildlifeComponent>(uid, out var wildlife) ||
            !TryComp<TransformComponent>(uid, out var transform) ||
            !_maps.TryGetMap(transform.MapID, out var map) ||
            !TryComp<KoronusPlanetSurfaceMapComponent>(map, out var surface) ||
            surface.SurfaceId != wildlife.Surface.Id || surface.AuthoredBaseGrids.Count == 0)
        {
            return false;
        }

        var position = _transform.GetWorldPosition(transform);
        var playableBounds = surface.PlayableBounds;
        if (!playableBounds.Contains(position))
            return false;

        foreach (var grid in surface.AuthoredBaseGrids)
        {
            if (!TryComp<MapGridComponent>(grid, out var baseGrid))
                continue;

            var local = Vector2.Transform(position, _transform.GetInvWorldMatrix(grid));
            // Remove fauna before it can reach the base or an outdoor landing pad.
            if (baseGrid.LocalAABB.Enlarged(12f).Contains(local))
                return false;
        }

        return true;
    }
}
