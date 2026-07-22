using Content.Shared.Durability;
using NUnit.Framework;

namespace Content.Tests.Shared.Durability;

[TestFixture]
public sealed class DurabilityMathTest
{
    [Test]
    public void RoundKeepsOneDecimalPlace()
    {
        Assert.That(DurabilityMath.Round(999.9000244140625f), Is.EqualTo(999.9f));
        Assert.That(DurabilityMath.Round(0.15f), Is.EqualTo(0.2f));
        Assert.That(DurabilityMath.Round(0.14f), Is.EqualTo(0.1f));
    }

    [Test]
    public void ClampKeepsDurabilityInValidRange()
    {
        Assert.That(DurabilityMath.Clamp(-1f, 500f), Is.EqualTo(0f));
        Assert.That(DurabilityMath.Clamp(500.06f, 500f), Is.EqualTo(500f));
        Assert.That(DurabilityMath.Clamp(123.456f, 500f), Is.EqualTo(123.5f));
    }

    [Test]
    public void FormatAlwaysUsesOneDecimalPlace()
    {
        Assert.That(DurabilityMath.Format(999.9000244140625f), Is.EqualTo("999.9"));
        Assert.That(DurabilityMath.Format(5f), Is.EqualTo("5.0"));
    }

    [Test]
    public void ArmorDrainScalesWithAbsorbedDamage()
    {
        Assert.That(DurabilityMath.CalculateArmorDamageDrain(12.34f, 0.5f), Is.EqualTo(6.2f));
        Assert.That(DurabilityMath.CalculateArmorDamageDrain(0.01f, 0.1f),
            Is.EqualTo(DurabilityMath.MinimumArmorDamageDrain));
    }

    [TestCase(0f, 1f)]
    [TestCase(-1f, 1f)]
    [TestCase(1f, 0f)]
    [TestCase(1f, -1f)]
    [TestCase(float.NaN, 1f)]
    [TestCase(1f, float.PositiveInfinity)]
    public void InvalidArmorDrainInputsDoNotSpendDurability(float absorbedDamage, float multiplier)
    {
        Assert.That(DurabilityMath.CalculateArmorDamageDrain(absorbedDamage, multiplier), Is.Zero);
    }
}
