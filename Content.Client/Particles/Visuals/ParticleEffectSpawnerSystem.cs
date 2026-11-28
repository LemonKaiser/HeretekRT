using Content.Shared.Particles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Client.Particles.Visuals;

/// <summary>
/// Runs particle samples placed through the ordinary entity spawn menu.
/// The marker is deliberately cosmetic-only and is not a generic replicated emitter component.
/// </summary>
public sealed partial class ParticleEffectSpawnerSystem : EntitySystem
{
    [Dependency] private ParticleSystem _particles = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, ActiveEmitter> _emitters = new();
    private readonly HashSet<EntityUid> _started = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ParticleEffectSpawnerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ParticleEffectSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ParticleEffectSpawnerComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
        SubscribeLocalEvent<ParticleEffectSpawnerComponent, ComponentShutdown>(OnShutdown);
        _particles.QualityChanged += OnParticleQualityChanged;
    }

    public override void Shutdown()
    {
        _particles.QualityChanged -= OnParticleQualityChanged;
        foreach (var emitter in _emitters.Values)
        {
            ParticleSystem.StopEffect(emitter);
        }

        _emitters.Clear();
        _started.Clear();
        base.Shutdown();
    }

    private void OnStartup(Entity<ParticleEffectSpawnerComponent> ent, ref ComponentStartup args)
    {
        Start(ent);
    }

    private void OnMapInit(Entity<ParticleEffectSpawnerComponent> ent, ref MapInitEvent args)
    {
        Start(ent);
    }

    private void OnAfterAutoHandleState(Entity<ParticleEffectSpawnerComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        // ComponentStartup can precede delivery of the replicated prototype fields. A mapper may also alter those
        // fields later, so restart this local cosmetic emitter from the authoritative component state.
        _started.Remove(ent.Owner);
        if (_emitters.Remove(ent.Owner, out var emitter))
            ParticleSystem.StopEffect(emitter);
        Start(ent);
    }

    private void OnParticleQualityChanged()
    {
        if (_particles.Quality == 0)
        {
            foreach (var emitter in _emitters.Values)
            {
                ParticleSystem.StopEffect(emitter);
            }

            _emitters.Clear();
            _started.Clear();
            return;
        }

        // Recreate only entries removed by the Off preset; _started makes ordinary quality changes a no-op.
        var query = EntityQueryEnumerator<ParticleEffectSpawnerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            Start((uid, component));
        }
    }

    private void Start(Entity<ParticleEffectSpawnerComponent> ent)
    {
        if (_started.Contains(ent.Owner) || TerminatingOrDeleted(ent.Owner))
            return;

        var coordinates = _transform.GetMapCoordinates(ent.Owner);
        if (coordinates.MapId == MapId.Nullspace)
            return;

        var parameters = new ParticleSpawnParameters(
            ent.Comp.Color,
            ent.Comp.EmitAngle,
            ent.Comp.Velocity,
            ent.Comp.Intensity,
            ent.Comp.Seed);

        if (ent.Comp.Count > 1)
        {
            if (_particles.SpawnBurst(ent.Comp.Effect, coordinates, ent.Comp.Count, parameters, ent.Comp.Attach ? ent.Owner : null) > 0)
                _started.Add(ent.Owner);
            return;
        }

        if (_particles.SpawnEffect(ent.Comp.Effect, coordinates, parameters, ent.Comp.Attach ? ent.Owner : null) is not { } emitter)
            return;

        _emitters[ent.Owner] = emitter;
        _started.Add(ent.Owner);
    }

    private void OnShutdown(Entity<ParticleEffectSpawnerComponent> ent, ref ComponentShutdown args)
    {
        _started.Remove(ent.Owner);
        if (_emitters.Remove(ent.Owner, out var emitter))
            ParticleSystem.StopEffect(emitter);
    }
}
