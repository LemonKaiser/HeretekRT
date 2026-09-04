using Content.Server.Particles;
using Content.Shared.Anomaly.Components;
using Content.Shared.Particles;

namespace Content.Server.Anomaly;

/// <summary>
/// Converts the existing authoritative anomaly pulse lifecycle into short PVS bursts.
/// No emitter is retained between pulses: the anomaly's normal appearance remains its
/// persistent presentation.
/// </summary>
public sealed class AnomalyParticleSystem : EntitySystem
{
    [Dependency] private readonly ParticleSpawnSystem _particles = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AnomalyComponent, AnomalyPulseEvent>(OnPulse);
        SubscribeLocalEvent<AnomalyComponent, AnomalySupercriticalEvent>(OnSupercritical);
    }

    private void OnPulse(Entity<AnomalyComponent> anomaly, ref AnomalyPulseEvent args)
    {
        _particles.Spawn(
            _transform.GetMapCoordinates(anomaly),
            "HrtAnomalyPulse",
            parameters: new ParticleSpawnParameters(Intensity: Math.Clamp(0.45f + args.Severity * 0.55f, 0.45f, 1f)),
            rateLimitSource: anomaly.Owner,
            cooldown: TimeSpan.FromMilliseconds(800));
    }

    private void OnSupercritical(Entity<AnomalyComponent> anomaly, ref AnomalySupercriticalEvent args)
    {
        _particles.Spawn(
            _transform.GetMapCoordinates(anomaly),
            "HrtAnomalySurge",
            parameters: new ParticleSpawnParameters(Intensity: Math.Clamp(args.PowerModifier, 0.7f, 1.35f)),
            rateLimitSource: anomaly.Owner,
            cooldown: TimeSpan.FromSeconds(1));
    }
}
