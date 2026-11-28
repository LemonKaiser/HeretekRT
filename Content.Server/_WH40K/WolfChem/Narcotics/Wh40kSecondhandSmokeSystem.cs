using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Smoking;
using Robust.Shared.Map;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// Second-hand smoke. Lit smokables and bong hits put smoke gas into the air and keep a share of what was smoked in a
/// reservoir at the source. Anyone breathing smoky air nearby draws from the reservoirs.
/// </summary>
public sealed partial class Wh40kSecondhandSmokeSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private InternalsSystem _internals = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ReactiveSystem _reactive = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    public const float CarryFraction = 0.15f;
    public const float MolesPerUnit = 0.3f;
    public const float DosePerMole = 0.05f;
    public const float MinMoles = 0.02f;
    public const float Range = 8f;
    public const float HalfLife = 90f;

    private const float Interval = 2f;

    private readonly Dictionary<EntityUid, Emission> _emissions = new();
    private readonly HashSet<Entity<RespiratorComponent>> _breathers = new();
    private readonly List<(Emission Emission, float Weight)> _sources = new();
    private readonly List<EntityUid> _spent = new();
    private float _accumulator;

    private sealed class Emission
    {
        public EntityCoordinates Coordinates;
        public readonly Solution Reservoir = new();
        public float Owed;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < Interval)
            return;

        _accumulator -= Interval;
        BurnSmokables(Interval);
        if (_emissions.Count == 0)
            return;

        Decay(Interval);

        _breathers.Clear();
        foreach (var emission in _emissions.Values)
        {
            _lookup.GetEntitiesInRange(emission.Coordinates, Range, _breathers);
        }

        foreach (var breather in _breathers)
        {
            if (_atmos.GetContainingMixture(breather.Owner) is { } air)
                Breathe(breather, air.GetMoles(Gas.Smoke), Interval);
        }
    }

    public void Exhale(EntityUid source, Solution smoked)
    {
        var burned = smoked.Volume;
        Release(source, burned, smoked.SplitSolution(burned * CarryFraction));
    }

    public void Breathe(EntityUid uid, float smokeMoles, float seconds)
    {
        if (smokeMoles < MinMoles
            || _mobState.IsDead(uid)
            || _internals.AreInternalsWorking(uid)
            || !HasComp<BloodstreamComponent>(uid))
            return;

        var coordinates = _transform.GetMoverCoordinates(uid);
        var total = 0f;
        _sources.Clear();
        foreach (var emission in _emissions.Values)
        {
            if (emission.Reservoir.Volume <= FixedPoint2.Zero
                || !coordinates.TryDistance(EntityManager, _transform, emission.Coordinates, out var distance)
                || distance > Range)
                continue;

            var weight = emission.Reservoir.Volume.Float() / (1f + distance);
            _sources.Add((emission, weight));
            total += weight;
        }

        if (total <= 0f)
            return;

        var dose = smokeMoles * DosePerMole * seconds;
        var inhaled = new Solution();
        foreach (var (emission, weight) in _sources)
        {
            var take = FixedPoint2.Min(emission.Reservoir.Volume, FixedPoint2.New(dose * weight / total));
            if (take > FixedPoint2.Zero)
                inhaled.AddSolution(emission.Reservoir.SplitSolution(take), null);
        }

        if (inhaled.Volume <= FixedPoint2.Zero)
            return;

        _reactive.DoEntityReaction(uid, inhaled, ReactionMethod.Ingestion);
        _bloodstream.TryAddToChemicals(uid, inhaled);
    }

    public FixedPoint2 Reservoir(EntityUid source)
    {
        return _emissions.TryGetValue(source, out var emission) ? emission.Reservoir.Volume : FixedPoint2.Zero;
    }

    private void BurnSmokables(float seconds)
    {
        var query = EntityQueryEnumerator<SmokableComponent>();
        while (query.MoveNext(out var uid, out var smokable))
        {
            if (smokable.State != SmokableState.Lit
                || !_solutions.TryGetSolution(uid, smokable.Solution, out var solution, out var contents)
                || contents.Volume <= FixedPoint2.Zero)
                continue;

            var burned = smokable.InhaleAmount * seconds;
            Release(uid, burned, _solutions.SplitSolution(solution.Value, burned * CarryFraction));
        }
    }

    private void Release(EntityUid source, FixedPoint2 burned, Solution carried)
    {
        if (_atmos.GetContainingMixture(source, excite: true) is { } air)
            air.AdjustMoles(Gas.Smoke, burned.Float() * MolesPerUnit);

        if (carried.Volume <= FixedPoint2.Zero)
            return;

        if (!_emissions.TryGetValue(source, out var emission))
        {
            emission = new Emission();
            _emissions[source] = emission;
        }

        emission.Coordinates = _transform.GetMoverCoordinates(source);
        emission.Reservoir.AddSolution(carried, null);
    }

    private void Decay(float seconds)
    {
        var lost = 1f - MathF.Pow(0.5f, seconds / HalfLife);
        _spent.Clear();
        foreach (var (source, emission) in _emissions)
        {
            emission.Owed += emission.Reservoir.Volume.Float() * lost;
            var amount = FixedPoint2.New(emission.Owed);
            if (amount > FixedPoint2.Zero)
            {
                emission.Reservoir.RemoveSolution(amount);
                emission.Owed -= amount.Float();
            }

            if (emission.Reservoir.Volume <= FixedPoint2.Zero || !emission.Coordinates.IsValid(EntityManager))
                _spent.Add(source);
        }

        foreach (var source in _spent)
        {
            _emissions.Remove(source);
        }
    }
}
