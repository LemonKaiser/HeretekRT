using Content.Server.Cargo.Systems;
using Content.Server.Power.Components;
using Content.Shared._Mono.CCVar;
using Content.Shared.Power.Components;
using Content.Shared.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Cleanup;

/// <summary>
/// This system cleans up small grid fragments that have less than a specified number of tiles after a delay.
/// </summary>
public sealed partial class GridCleanupSystem : BaseCleanupSystem<MapGridComponent>
{
    [Dependency] private CleanupHelperSystem _cleanup = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IGameTiming _timing = default!;

    private float _maxDistance;
    private float _maxValue;
    private int _aggressiveTiles;
    private TimeSpan _duration;

    private HashSet<Entity<ApcComponent>> _apcList = new();

    private EntityQuery<BatteryComponent> _batteryQuery;
    private EntityQuery<CleanupImmuneComponent> _immuneQuery;

    public override void Initialize()
    {
        base.Initialize();

        _batteryQuery = GetEntityQuery<BatteryComponent>();
        _immuneQuery = GetEntityQuery<CleanupImmuneComponent>();

        Subs.CVar(_cfg, MonoCVars.GridCleanupDistance, val => _maxDistance = val, true);
        Subs.CVar(_cfg, MonoCVars.GridCleanupMaxValue, val => _maxValue = val, true);
        Subs.CVar(_cfg, MonoCVars.GridCleanupDuration, val => _duration = TimeSpan.FromSeconds(val), true);
        Subs.CVar(_cfg, MonoCVars.GridCleanupAggressiveTiles, val => _aggressiveTiles = val, true);
    }

    protected override bool ShouldEntityCleanup(EntityUid uid)
    {
        var xform = Transform(uid);
        // if we somehow lost it
        if (!TryComp<MapGridComponent>(uid, out var grid) || !TryComp<PhysicsComponent>(uid, out var body))
            return false;

        var parent = xform.ParentUid;

        var state = EnsureComp<GridCleanupGridComponent>(uid);
        var now = _timing.CurTime;

        var tiles = body.FixturesMass / ShuttleSystem.TileDensityMultiplier;
        var aggressiveTiles = Math.Max(_aggressiveTiles, 1);
        var scale = Math.Clamp(tiles / aggressiveTiles, 0.1f, 1f);

        if (HasComp<MapComponent>(uid) // if we're a planetmap ignore
            || HasComp<MapGridComponent>(parent) // do not delete anything on planetmaps either
            || _cleanup.IsGridProtectedFromCleanup(uid)
            || !state.IgnoreIFF && TryComp<IFFComponent>(uid, out var iff) && (iff.Flags & IFFFlags.HideLabel) == 0 // delete only if IFF off
            || _cleanup.HasNearbyPlayers(xform.Coordinates, state.DistanceOverride ?? _maxDistance * scale * scale) // square it
            || !state.IgnorePowered && HasPoweredAPC((uid, xform)) // don't delete if it has powered APCs
            || !state.IgnorePrice && _pricing.AppraiseGrid(uid) > _maxValue) // expensive to run, put last
        {
            state.CleanupAccumulator = TimeSpan.FromSeconds(0);
            state.LastEvaluation = now;
            state.EligibilityActive = false;
            return false;
        }

        if (!state.EligibilityActive)
        {
            state.EligibilityActive = true;
            state.LastEvaluation = now;
            return state.CleanupAccumulator >= _duration;
        }

        var elapsed = state.LastEvaluation == TimeSpan.Zero
            ? TimeSpan.Zero
            : now - state.LastEvaluation;
        state.LastEvaluation = now;

        // A server stall or cold restore must not make a cleanup timer jump forward.
        elapsed = elapsed > _cleanupInterval * 2 ? _cleanupInterval : elapsed;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (state.CleanupAccumulator < _duration)
        {
            var acceleration = MathF.Max(state.CleanupAcceleration, 0.01f);
            state.CleanupAccumulator += TimeSpan.FromSeconds(elapsed.TotalSeconds * acceleration / scale);
        }

        return state.CleanupAccumulator >= _duration;
    }

    bool HasPoweredAPC(Entity<TransformComponent> grid)
    {
        _apcList.Clear();
        var worldAABB = _lookup.GetWorldAABB(grid, grid.Comp);

        _lookup.GetEntitiesIntersecting<ApcComponent>(grid.Comp.MapID, worldAABB, _apcList);

        foreach (var apc in _apcList)
        {
            // charge check should ideally be a comparision to 0f but i don't trust that
            if (_batteryQuery.TryComp(apc, out var battery)
                && battery.CurrentCharge > battery.MaxCharge * 0.01f
                && apc.Comp.MainBreakerEnabled // if it's disabled consider it depowered
            )
                return true;
        }
        return false;
    }
}
