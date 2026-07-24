using Content.Server.Xenoarchaeology.XenoArtifacts;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Cleans up temporary high-risk entities only on grids with the corresponding local safety rule.
/// The delay lets ordinary spawning and gameplay complete before Footfall removes the hazard.
/// </summary>
public sealed class KoronusSafetyHazardCleanupSystem : EntitySystem
{
    private static readonly TimeSpan CleanupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _artifactCleanupAt = [];
    private readonly Dictionary<EntityUid, TimeSpan> _hostileNpcCleanupAt = [];
    private TimeSpan _nextScan;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextScan)
            return;

        _nextScan = _timing.CurTime + ScanInterval;
        UpdateArtifacts(_timing.CurTime);
        UpdateHostileNpcs(_timing.CurTime);
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

    private void UpdateHostileNpcs(TimeSpan now)
    {
        var npcs = EntityQueryEnumerator<ActiveNPCComponent, NpcFactionMemberComponent>();
        while (npcs.MoveNext(out var uid, out _, out var faction))
        {
            Entity<NpcFactionMemberComponent?> factionEntity = new(uid, faction);
            if (_safety.HasRule(uid, KoronusSafetyRule.HostileNpcAutoCleanup) &&
                _npcFaction.IsFactionHostile("NanoTrasen", factionEntity))
            {
                _hostileNpcCleanupAt.TryAdd(uid, now + CleanupDelay);
            }
        }

        foreach (var (uid, cleanupAt) in _hostileNpcCleanupAt.ToArray())
        {
            if (TerminatingOrDeleted(uid) ||
                !TryComp<NpcFactionMemberComponent>(uid, out var faction) ||
                !HasComp<ActiveNPCComponent>(uid) ||
                !_safety.HasRule(uid, KoronusSafetyRule.HostileNpcAutoCleanup))
            {
                _hostileNpcCleanupAt.Remove(uid);
                continue;
            }

            Entity<NpcFactionMemberComponent?> factionEntity = new(uid, faction);
            if (!_npcFaction.IsFactionHostile("NanoTrasen", factionEntity))
            {
                _hostileNpcCleanupAt.Remove(uid);
                continue;
            }

            if (cleanupAt > now)
                continue;

            QueueDel(uid);
            _hostileNpcCleanupAt.Remove(uid);
        }
    }
}
