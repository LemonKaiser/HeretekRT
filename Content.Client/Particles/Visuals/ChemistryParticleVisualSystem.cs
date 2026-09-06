using Content.Client.Chemistry.Visualizers;
using Content.Shared.Particles;
using Content.Shared.Smoking;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client.Particles.Visuals;

/// <summary>
/// Adds a small client-only particle layer to replicated chemical clouds. Emitters live
/// exactly as long as the existing smoke or foam visual components, so no per-tile state
/// is created on the server.
/// </summary>
public sealed partial class ChemistryParticleVisualSystem : EntitySystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private ParticleSystem _particles = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly ProtoId<ParticleEffectPrototype> SmokeEffect = "HrtToxicFumes";
    private static readonly ProtoId<ParticleEffectPrototype> FoamEffect = "HrtFoamBubblesContinuous";

    private readonly Dictionary<EntityUid, ActiveEmitter> _emitters = new();
    private float _colorRefreshAccumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SmokeVisualsComponent, MapInitEvent>(OnSmokeMapInit);
        SubscribeLocalEvent<FoamVisualsComponent, MapInitEvent>(OnFoamMapInit);
        SubscribeLocalEvent<SmokeVisualsComponent, ComponentShutdown>(OnSmokeVisualShutdown);
        SubscribeLocalEvent<FoamVisualsComponent, ComponentShutdown>(OnFoamVisualShutdown);
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
        base.Shutdown();
    }

    private void OnSmokeMapInit(Entity<SmokeVisualsComponent> ent, ref MapInitEvent args)
    {
        // Foam inherits SmokeVisuals solely for tinting; it gets bubbles instead of a second smoke emitter.
        if (!HasComp<FoamVisualsComponent>(ent))
            EnsureEmitter(ent.Owner, SmokeEffect, GetSmokeColor(ent));
    }

    private void OnFoamMapInit(Entity<FoamVisualsComponent> ent, ref MapInitEvent args)
    {
        EnsureEmitter(ent.Owner, FoamEffect, null);
    }

    private void OnSmokeVisualShutdown(Entity<SmokeVisualsComponent> ent, ref ComponentShutdown args)
    {
        StopEmitter(ent.Owner);
    }

    private void OnFoamVisualShutdown(Entity<FoamVisualsComponent> ent, ref ComponentShutdown args)
    {
        StopEmitter(ent.Owner);
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
            return;
        }

        var smokeQuery = EntityQueryEnumerator<SmokeVisualsComponent>();
        while (smokeQuery.MoveNext(out var uid, out var smoke))
        {
            if (!HasComp<FoamVisualsComponent>(uid))
                EnsureEmitter(uid, SmokeEffect, GetSmokeColor((uid, smoke)));
        }

        var foamQuery = EntityQueryEnumerator<FoamVisualsComponent>();
        while (foamQuery.MoveNext(out var uid, out _))
        {
            EnsureEmitter(uid, FoamEffect, null);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // SmokeVisualsComponent already owns AppearanceChangeEvent through SmokeVisualizerSystem. Refreshing at a
        // modest cadence avoids a duplicate directed-event subscription while preserving the replicated reagent tint.
        _colorRefreshAccumulator += frameTime;
        if (_colorRefreshAccumulator < 0.2f)
            return;

        _colorRefreshAccumulator = 0f;
        foreach (var (uid, emitter) in _emitters)
        {
            if (!TryComp<SmokeVisualsComponent>(uid, out var smoke) || HasComp<FoamVisualsComponent>(uid))
                continue;

            emitter.ColorOverride = GetSmokeColor((uid, smoke));
        }
    }

    private void EnsureEmitter(EntityUid uid, ProtoId<ParticleEffectPrototype> effect, Color? color)
    {
        if (_particles.Quality == 0 || _emitters.ContainsKey(uid))
            return;

        var coords = _transform.GetMapCoordinates(uid);
        if (_particles.SpawnEffect(effect, coords, new ParticleSpawnParameters(Color: color, Intensity: 0.45f), uid) is { } emitter)
            _emitters.Add(uid, emitter);
    }

    private void StopEmitter(EntityUid uid)
    {
        if (_emitters.Remove(uid, out var emitter))
            ParticleSystem.StopEffect(emitter);
    }

    private Color? GetSmokeColor(Entity<SmokeVisualsComponent> smoke, AppearanceComponent? appearance = null)
    {
        return _appearance.TryGetData(smoke, SmokeVisuals.Color, out Color color, appearance)
            ? color
            : null;
    }
}
