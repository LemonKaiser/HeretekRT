using System.Numerics;
using System.Linq;
using Content.Shared.Particles;

namespace Content.Client.Particles;

/// <summary>
/// Per-frame measurements exposed for profiling and the <c>particlestats</c> command.
/// </summary>
public readonly record struct ParticleStatistics(
    int ActiveEmitters,
    int LiveParticles,
    float LiveCost,
    int SimulatedParticles,
    int EmittedParticles,
    int CulledParticles,
    int DrawCalls,
    int DrawnParticles,
    float SimulationMilliseconds,
    float RenderPreparationMilliseconds);

/// <summary>
/// Immutable runtime representation of the curves used by an effect. Curves are sorted and
/// baked once on spawn/prototype reload instead of searched for every particle on every frame.
/// </summary>
internal sealed class CompiledParticleEffect
{
    private const int CurveSamples = 128;

    public readonly float[]? AlphaOverLifetime;
    public readonly float[]? SizeOverLifetime;
    public readonly float[]? SpeedOverLifetime;
    public readonly float[]? EmissionOverTime;
    public readonly Vector2[]? ForceOverLifetime;
    public readonly Vector2[]? VelocityOverLifetime;
    public readonly Color[]? ColorOverLifetime;

    public CompiledParticleEffect(ParticleEffectPrototype proto)
    {
        AlphaOverLifetime = BakeFloatCurve(proto.AlphaOverLifetime);
        SizeOverLifetime = BakeFloatCurve(proto.SizeOverLifetime);
        SpeedOverLifetime = BakeFloatCurve(proto.SpeedOverLifetime);
        EmissionOverTime = BakeFloatCurve(proto.EmissionOverTime);
        ForceOverLifetime = BakeVectorCurve(proto.ForceOverLifetime);
        VelocityOverLifetime = BakeVectorCurve(proto.VelocityOverLifetime);
        ColorOverLifetime = BakeColorCurve(proto.ColorOverLifetime);
    }

    public static float Sample(float[]? samples, float t, float fallback)
    {
        if (samples == null)
            return fallback;

        var position = Math.Clamp(t, 0f, 1f) * (samples.Length - 1);
        var left = (int) position;
        var right = Math.Min(left + 1, samples.Length - 1);
        return samples[left] + (samples[right] - samples[left]) * (position - left);
    }

    public static Vector2 Sample(Vector2[]? samples, float t)
    {
        if (samples == null)
            return Vector2.Zero;

        var position = Math.Clamp(t, 0f, 1f) * (samples.Length - 1);
        var left = (int) position;
        var right = Math.Min(left + 1, samples.Length - 1);
        return Vector2.Lerp(samples[left], samples[right], position - left);
    }

    public static Color Sample(Color[]? samples, float t, Color fallback)
    {
        if (samples == null)
            return fallback;

        var position = Math.Clamp(t, 0f, 1f) * (samples.Length - 1);
        var left = (int) position;
        var right = Math.Min(left + 1, samples.Length - 1);
        return Color.InterpolateBetween(samples[left], samples[right], position - left);
    }

    private static float[]? BakeFloatCurve(List<ParticleCurveKey> source)
    {
        if (source.Count == 0)
            return null;

        var keys = source
            .Where(key => float.IsFinite(key.Time) && float.IsFinite(key.Value))
            .OrderBy(key => key.Time)
            .ToArray();

        if (keys.Length == 0)
            return null;

        var samples = new float[CurveSamples];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = SampleFloatKeys(keys, i / (float) (samples.Length - 1));
        return samples;
    }

    private static Vector2[]? BakeVectorCurve(List<Vector2CurveKey> source)
    {
        if (source.Count == 0)
            return null;

        var keys = source
            .Where(key => float.IsFinite(key.Time) && float.IsFinite(key.Value.X) && float.IsFinite(key.Value.Y))
            .OrderBy(key => key.Time)
            .ToArray();

        if (keys.Length == 0)
            return null;

        var samples = new Vector2[CurveSamples];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = SampleVectorKeys(keys, i / (float) (samples.Length - 1));
        return samples;
    }

    private static Color[]? BakeColorCurve(List<ColorCurveKey> source)
    {
        if (source.Count == 0)
            return null;

        var keys = source
            .Where(key => float.IsFinite(key.Time))
            .OrderBy(key => key.Time)
            .ToArray();

        if (keys.Length == 0)
            return null;

        var samples = new Color[CurveSamples];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = SampleColorKeys(keys, i / (float) (samples.Length - 1));
        return samples;
    }

    private static float SampleFloatKeys(ParticleCurveKey[] keys, float t)
    {
        if (t <= keys[0].Time)
            return keys[0].Value;

        for (var i = 1; i < keys.Length; i++)
        {
            if (t > keys[i].Time)
                continue;

            var previous = keys[i - 1];
            var next = keys[i];
            var span = next.Time - previous.Time;
            return span <= 0f
                ? next.Value
                : previous.Value + (next.Value - previous.Value) * ((t - previous.Time) / span);
        }

        return keys[^1].Value;
    }

    private static Vector2 SampleVectorKeys(Vector2CurveKey[] keys, float t)
    {
        if (t <= keys[0].Time)
            return keys[0].Value;

        for (var i = 1; i < keys.Length; i++)
        {
            if (t > keys[i].Time)
                continue;

            var previous = keys[i - 1];
            var next = keys[i];
            var span = next.Time - previous.Time;
            return span <= 0f
                ? next.Value
                : Vector2.Lerp(previous.Value, next.Value, (t - previous.Time) / span);
        }

        return keys[^1].Value;
    }

    private static Color SampleColorKeys(ColorCurveKey[] keys, float t)
    {
        if (t <= keys[0].Time)
            return keys[0].Color;

        for (var i = 1; i < keys.Length; i++)
        {
            if (t > keys[i].Time)
                continue;

            var previous = keys[i - 1];
            var next = keys[i];
            var span = next.Time - previous.Time;
            return span <= 0f
                ? next.Color
                : Color.InterpolateBetween(previous.Color, next.Color, (t - previous.Time) / span);
        }

        return keys[^1].Color;
    }
}

internal static class ParticleRuntimeMath
{
    public const float MaxSimulationDeltaSeconds = 0.05f;
    public const float MaxEmissionDeltaSeconds = 0.1f;
    public const int MaxParticlesSpawnedPerFrame = 512;

    public static ParticlePriority ResolvePriority(ParticleEffectPrototype proto)
        => proto.IgnoreQualitySettings ? ParticlePriority.Critical : proto.Priority;

    public static float GetQualityMultiplier(ParticlePriority priority, int quality)
    {
        if (quality <= 0)
            return 0f;

        quality = Math.Min(quality, 3);
        return priority switch
        {
            ParticlePriority.Critical => 1f,
            ParticlePriority.Important => quality switch { 1 => 0.7f, 2 => 0.9f, _ => 1f },
            ParticlePriority.Normal => quality switch { 1 => 0.4f, 2 => 0.7f, _ => 1f },
            _ => quality switch { 1 => 0.2f, 2 => 0.5f, _ => 1f },
        };
    }

    public static float GetDistanceMultiplier(float distance, float maxDistance, float boundsRadius)
    {
        if (maxDistance <= 0f)
            return 1f;

        var adjustedDistance = Math.Max(0f, distance - Math.Max(0f, boundsRadius));
        if (adjustedDistance >= maxDistance)
            return 0f;

        var fadeStart = maxDistance * 0.7f;
        if (adjustedDistance <= fadeStart)
            return 1f;

        return (maxDistance - adjustedDistance) / Math.Max(maxDistance - fadeStart, float.Epsilon);
    }

    public static float ClampSimulationDelta(float frameTime)
        => Math.Clamp(frameTime, 0f, MaxSimulationDeltaSeconds);

    public static float ClampEmissionDelta(float frameTime)
        => Math.Clamp(frameTime, 0f, MaxEmissionDeltaSeconds);

    public static float GetParticleCost(ParticleEffectPrototype proto)
        => Math.Clamp(proto.Cost, 0.1f, 10f);
}
