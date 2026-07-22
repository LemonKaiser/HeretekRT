using Content.Server.Fluids.EntitySystems;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.SectorMap.Systems;

/// <summary>
/// Applies the local anti-flooding rule without changing ordinary puddle behaviour elsewhere.
/// </summary>
public sealed class KoronusSafetyPuddleSystem : EntitySystem
{
    private static readonly TimeSpan CleanupDelay = TimeSpan.FromMinutes(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private KoronusSafetyPolicySystem _safety = default!;
    [Dependency] private PuddleSystem _puddles = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PuddleComponent, ComponentStartup>(OnPuddleStartup);
        SubscribeLocalEvent<KoronusSafetyPuddleCleanupComponent, SolutionContainerChangedEvent>(OnPuddleSolutionChanged);
    }

    private void OnPuddleStartup(EntityUid uid, PuddleComponent component, ComponentStartup args)
    {
        if (_safety.HasRule(uid, KoronusSafetyRule.PuddleAutoCleanup))
            EnsureComp<KoronusSafetyPuddleCleanupComponent>(uid).CleanupAt = _timing.CurTime + CleanupDelay;
    }

    private void OnPuddleSolutionChanged(
        EntityUid uid,
        KoronusSafetyPuddleCleanupComponent cleanup,
        ref SolutionContainerChangedEvent args)
    {
        if (!TryComp<PuddleComponent>(uid, out var puddle) ||
            args.SolutionId != puddle.SolutionName ||
            args.Solution.Volume <= 0)
            return;

        if (!_safety.HasRule(uid, KoronusSafetyRule.PuddleAutoCleanup))
        {
            RemCompDeferred<KoronusSafetyPuddleCleanupComponent>(uid);
            return;
        }

        cleanup.CleanupAt = _timing.CurTime + CleanupDelay;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var puddles = EntityQueryEnumerator<KoronusSafetyPuddleCleanupComponent, PuddleComponent>();
        while (puddles.MoveNext(out var uid, out var cleanup, out var puddle))
        {
            if (!_safety.HasRule(uid, KoronusSafetyRule.PuddleAutoCleanup))
            {
                RemCompDeferred<KoronusSafetyPuddleCleanupComponent>(uid);
                continue;
            }

            if (cleanup.CleanupAt > now)
                continue;

            _puddles.ClearPuddle((uid, puddle));
            RemCompDeferred<KoronusSafetyPuddleCleanupComponent>(uid);
        }
    }
}
