using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Particles;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client.Particles.Visuals;

/// <summary>
/// Particles when entities are on fire.
/// </summary>
public sealed partial class FlammableParticleSystem : EntitySystem
{
    [Dependency] private ParticleSystem _particles = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly ProtoId<ParticleEffectPrototype> FireEffect = "HrtFireContinuous";
    private static readonly ProtoId<ParticleEffectPrototype> SmokeEffect = "HrtFireSmoke";
    private static readonly ProtoId<ParticleEffectPrototype> EmbersEffect = "HrtFireEmbersContinuous";

    private sealed class FireState
    {
        public ActiveEmitter? FireEmitter;
        public ActiveEmitter? SmokeEmitter;
        public ActiveEmitter? EmbersEmitter;
        public bool OnFire;
        public float Intensity = 1f;
    }

    private readonly Dictionary<EntityUid, FireState> _active = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlammableComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<FlammableComponent, ComponentShutdown>(OnShutdown);
        _particles.QualityChanged += OnParticleQualityChanged;
    }

    public override void Shutdown()
    {
        _particles.QualityChanged -= OnParticleQualityChanged;

        foreach (var state in _active.Values)
        {
            StopState(state);
        }

        _active.Clear();
        base.Shutdown();
    }

    private void OnAppearanceChange(Entity<FlammableComponent> ent, ref AppearanceChangeEvent args)
    {
        if (!_appearance.TryGetData(ent, FireVisuals.OnFire, out bool onFire))
            onFire = false;

        if (!_appearance.TryGetData(ent, FireVisuals.FireStacks, out float stacks))
            stacks = 0f;

        _active.TryGetValue(ent, out var state);

        if (onFire)
        {
            if (state == null)
            {
                state = new FireState();
                _active[ent] = state;
            }

            if (!state.OnFire)
            {
                state.OnFire = true;
            }

            var maximumStacks = MathF.Max(ent.Comp.MaximumFireStacks, 1f);
            var intensity = Math.Clamp(stacks / maximumStacks * 2f, 1f, 2f);
            state.Intensity = intensity;
            EnsureEmitters(ent.Owner, state);

            if (state.FireEmitter != null)
                state.FireEmitter.Intensity = intensity;
            if (state.SmokeEmitter != null)
                state.SmokeEmitter.Intensity = intensity;
            if (state.EmbersEmitter != null)
                state.EmbersEmitter.Intensity = intensity;
        }
        else if (state is { OnFire: true })
        {
            _active.Remove(ent);
            StopState(state);
        }
    }

    private void OnShutdown(Entity<FlammableComponent> ent, ref ComponentShutdown args)
    {
        if (_active.Remove(ent, out var state))
            StopState(state);
    }

    private void OnParticleQualityChanged()
    {
        foreach (var (uid, state) in _active)
        {
            if (!state.OnFire)
                continue;

            if (_particles.Quality == 0)
            {
                StopState(state);
                continue;
            }

            EnsureEmitters(uid, state);
        }
    }

    private void EnsureEmitters(EntityUid uid, FireState state)
    {
        if (_particles.Quality == 0)
            return;

        var coords = _transform.GetMapCoordinates(uid);
        state.SmokeEmitter ??= _particles.SpawnEffect(SmokeEffect, coords, uid);
        state.FireEmitter ??= _particles.SpawnEffect(FireEffect, coords, uid);
        state.EmbersEmitter ??= _particles.SpawnEffect(EmbersEffect, coords, uid);

        if (state.SmokeEmitter != null)
            state.SmokeEmitter.Intensity = state.Intensity;
        if (state.FireEmitter != null)
            state.FireEmitter.Intensity = state.Intensity;
        if (state.EmbersEmitter != null)
            state.EmbersEmitter.Intensity = state.Intensity;
    }

    private void StopState(FireState state)
    {
        if (state.FireEmitter != null)
        {
            state.FireEmitter.Exhausted = true;
            state.FireEmitter = null;
        }

        if (state.SmokeEmitter != null)
        {
            state.SmokeEmitter.Exhausted = true;
            state.SmokeEmitter = null;
        }

        if (state.EmbersEmitter != null)
        {
            state.EmbersEmitter.Exhausted = true;
            state.EmbersEmitter = null;
        }
    }
}
