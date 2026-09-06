using Content.Client.Particles;
using Content.Shared._WH40K.SectorMap.Teleporters;
using Content.Shared.Particles;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.SectorMap.Teleporters;

/// <summary>
/// Owns short client-side teleporter charge emitters. They are bounded by the server-authoritative spin-up duration
/// so a missed stop notification cannot leave a permanent effect behind.
/// </summary>
public sealed partial class KoronusPlanetaryTeleporterParticleSystem : EntitySystem
{
    [Dependency] private ParticleSystem _particles = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private sealed class ChargeState
    {
        public uint Generation;
        public uint EmitterHandle;
    }

    private readonly Dictionary<EntityUid, ChargeState> _chargeEmitters = new();
    private uint _nextChargeGeneration;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<KoronusPlanetaryTeleporterChargeParticlesEvent>(OnChargeParticles);
        _particles.QualityChanged += OnParticleQualityChanged;
    }

    public override void Shutdown()
    {
        _particles.QualityChanged -= OnParticleQualityChanged;
        foreach (var state in _chargeEmitters.Values)
            _particles.StopEffect(state.EmitterHandle);

        _chargeEmitters.Clear();
        base.Shutdown();
    }

    private void OnChargeParticles(KoronusPlanetaryTeleporterChargeParticlesEvent ev)
    {
        var teleporter = GetEntity(ev.Teleporter);
        if (!Exists(teleporter))
            return;

        StopCharge(teleporter);
        if (!ev.Active)
            return;

        var state = new ChargeState { Generation = ++_nextChargeGeneration };
        _chargeEmitters[teleporter] = state;
        StartCharge(teleporter, state);
        var generation = state.Generation;
        Timer.Spawn(TimeSpan.FromSeconds(Math.Max(0.1f, ev.Duration) + 0.15f), () => StopCharge(teleporter, generation));
    }

    private void OnParticleQualityChanged()
    {
        foreach (var (teleporter, state) in _chargeEmitters)
        {
            if (_particles.Quality == 0)
            {
                _particles.StopEffect(state.EmitterHandle);
                state.EmitterHandle = 0;
                continue;
            }

            if (state.EmitterHandle == 0)
                StartCharge(teleporter, state);
        }
    }

    private void StartCharge(EntityUid teleporter, ChargeState state)
    {
        if (_particles.Quality == 0 || !Exists(teleporter))
            return;

        var emitter = _particles.SpawnEffect(
            new Robust.Shared.Prototypes.ProtoId<ParticleEffectPrototype>("HrtTeleporterCharge"),
            _transform.GetMapCoordinates(teleporter),
            new ParticleSpawnParameters(Intensity: 0.8f),
            teleporter);
        if (emitter == null)
            return;

        state.EmitterHandle = emitter.Handle;
    }

    private void StopCharge(EntityUid teleporter, uint? expectedGeneration = null)
    {
        if (!_chargeEmitters.TryGetValue(teleporter, out var state) ||
            expectedGeneration is { } expected && expected != state.Generation)
        {
            return;
        }

        _particles.StopEffect(state.EmitterHandle);
        _chargeEmitters.Remove(teleporter);
    }
}
