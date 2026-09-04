namespace Content.Shared.Particles;

/// <summary>
/// Shared validation rules for cosmetic one-shot particle requests.
/// The server applies them before sending a PVS event and the client applies them again
/// before creating emitters, so a malformed event cannot turn into an unbounded burst.
/// </summary>
public static class ParticleSpawnLimits
{
    public const int MaxEmittersPerEvent = 16;
    public const float MaxLiveParticleCostPerEvent = 120f;
    public const float MinIntensity = 0.05f;
    public const float MaxIntensity = 4f;
    private const float MinParticleCost = 0.1f;
    private const float MaxParticleCost = 10f;

    /// <summary>
    /// Validates and clamps presentation parameters received from game code or over the network.
    /// </summary>
    public static bool TryNormalize(ParticleSpawnParameters? parameters, out ParticleSpawnParameters normalized)
    {
        var value = parameters ?? ParticleSpawnParameters.Default;
        if (!float.IsFinite(value.Intensity) || value.Intensity <= 0f ||
            value.EmitAngle is { } angle && !double.IsFinite(angle.Theta) ||
            value.Velocity is { } velocity &&
            (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y)))
        {
            normalized = ParticleSpawnParameters.Default;
            return false;
        }

        normalized = value with { Intensity = Math.Clamp(value.Intensity, MinIntensity, MaxIntensity) };
        return true;
    }

    /// <summary>
    /// Limits a network request to burst effects and a bounded sum of their maximum live particle cost.
    /// Continuous effects are intentionally represented by feature-specific replicated state instead.
    /// </summary>
    public static int ClampEmitterCount(ParticleEffectPrototype prototype, int requestedCount)
    {
        if (!prototype.Burst || requestedCount <= 0)
            return 0;

        var particleCost = Math.Clamp(prototype.Cost, MinParticleCost, MaxParticleCost);
        var emitterCost = Math.Max(1, prototype.MaxCount) * particleCost;
        var byCost = (int) MathF.Floor(MaxLiveParticleCostPerEvent / emitterCost);
        return Math.Clamp(requestedCount, 0, Math.Min(MaxEmittersPerEvent, byCost));
    }

    /// <summary>
    /// Produces a stable, distinct seed for every emitter in one replicated burst.
    /// </summary>
    public static int DeriveSeed(int seed, int sequence)
    {
        unchecked
        {
            var value = (uint) seed + 0x9E3779B9u * (uint) (sequence + 1);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int) value;
        }
    }
}
