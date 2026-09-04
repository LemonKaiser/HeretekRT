using Content.Client.Particles;
using Content.Shared.Particles;
using NUnit.Framework;

namespace Content.Tests.Client.Particles;

[TestFixture]
public sealed class ParticleRuntimeMathTest
{
    [Test]
    public void QualityPriorityScalingIsOrderedAndDisablesAtOff()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParticleRuntimeMath.GetQualityMultiplier(ParticlePriority.Critical, 0), Is.Zero);
            Assert.That(ParticleRuntimeMath.GetQualityMultiplier(ParticlePriority.Decorative, 0), Is.Zero);
            Assert.That(ParticleRuntimeMath.GetQualityMultiplier(ParticlePriority.Critical, 1), Is.EqualTo(1f));
            Assert.That(ParticleRuntimeMath.GetQualityMultiplier(ParticlePriority.Important, 1), Is.EqualTo(0.7f));
            Assert.That(ParticleRuntimeMath.GetQualityMultiplier(ParticlePriority.Normal, 1), Is.EqualTo(0.4f));
            Assert.That(ParticleRuntimeMath.GetQualityMultiplier(ParticlePriority.Decorative, 1), Is.EqualTo(0.2f));
            Assert.That(ParticleRuntimeMath.GetQualityMultiplier(ParticlePriority.Decorative, 3), Is.EqualTo(1f));
        });
    }

    [Test]
    public void DistanceLodHonoursBoundsAndFadesOnlyNearTheLimit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParticleRuntimeMath.GetDistanceMultiplier(100f, 0f, 0f), Is.EqualTo(1f));
            Assert.That(ParticleRuntimeMath.GetDistanceMultiplier(5f, 20f, 1f), Is.EqualTo(1f));
            Assert.That(ParticleRuntimeMath.GetDistanceMultiplier(16f, 20f, 1f), Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(ParticleRuntimeMath.GetDistanceMultiplier(21f, 20f, 1f), Is.Zero);
        });
    }

    [Test]
    public void SimulationAndEmissionDeltaAreBounded()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParticleRuntimeMath.ClampSimulationDelta(-1f), Is.Zero);
            Assert.That(ParticleRuntimeMath.ClampSimulationDelta(10f), Is.EqualTo(ParticleRuntimeMath.MaxSimulationDeltaSeconds));
            Assert.That(ParticleRuntimeMath.ClampEmissionDelta(-1f), Is.Zero);
            Assert.That(ParticleRuntimeMath.ClampEmissionDelta(10f), Is.EqualTo(ParticleRuntimeMath.MaxEmissionDeltaSeconds));
        });
    }

    [Test]
    public void ParticleAgeRatioUsesStoredInverseLifetime()
    {
        var particle = new ParticleData
        {
            Age = 0.5f,
            Lifetime = 2f,
            InverseLifetime = 0.5f,
        };

        Assert.That(particle.AgeRatio, Is.EqualTo(0.25f));
    }

    [Test]
    public void SpawnParametersClampIntensityAndRejectInvalidValues()
    {
        Assert.That(
            ParticleSpawnLimits.TryNormalize(new ParticleSpawnParameters(Intensity: 100f), out var normalized),
            Is.True);
        Assert.That(normalized.Intensity, Is.EqualTo(ParticleSpawnLimits.MaxIntensity));

        Assert.Multiple(() =>
        {
            Assert.That(
                ParticleSpawnLimits.TryNormalize(new ParticleSpawnParameters(Intensity: 0f), out _),
                Is.False);
            Assert.That(
                ParticleSpawnLimits.TryNormalize(new ParticleSpawnParameters(Intensity: float.NaN), out _),
                Is.False);
        });
    }

    [Test]
    public void DerivedBurstSeedsAreStableAndDistinct()
    {
        var first = ParticleSpawnLimits.DeriveSeed(12345, 0);
        var second = ParticleSpawnLimits.DeriveSeed(12345, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ParticleSpawnLimits.DeriveSeed(12345, 0), Is.EqualTo(first));
            Assert.That(second, Is.Not.EqualTo(first));
        });
    }
}
