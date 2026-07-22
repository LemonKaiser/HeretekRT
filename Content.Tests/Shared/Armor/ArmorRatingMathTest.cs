using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests.Shared.Armor;

[TestFixture]
public sealed class ArmorRatingMathTest
{
    [Test]
    public void PercentageModifiersKeepTheirOriginalValues()
    {
        var modifiers = new DamageModifierSet();
        modifiers.Coefficients["Blunt"] = 0.5f;
        modifiers.Coefficients["Heat"] = 0.2f;
        modifiers.Coefficients["Radiation"] = 0f;
        modifiers.Coefficients["Shock"] = 1.5f;

        var damage = CreateDamage(("Blunt", 20f), ("Heat", 10f), ("Radiation", 5f), ("Shock", 10f));
        var result = DamageSpecifier.ApplyModifierSet(damage, modifiers);

        Assert.Multiple(() =>
        {
            Assert.That(result.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(10f)));
            Assert.That(result.DamageDict["Heat"], Is.EqualTo(FixedPoint2.New(2f)));
            Assert.That(result.DamageDict.ContainsKey("Radiation"), Is.False);
            Assert.That(result.DamageDict["Shock"], Is.EqualTo(FixedPoint2.New(15f)));
        });
    }

    [Test]
    public void PercentageModifiersAreNotPenetratedByFlatArmorPenetration()
    {
        var modifiers = new DamageModifierSet();
        modifiers.Coefficients["Piercing"] = 0.5f;

        var damage = CreateDamage(("Piercing", 20f));
        var afterPercentage = DamageSpecifier.ApplyModifierSet(damage, modifiers);
        var result = ArmorRatingMath.Apply(afterPercentage, 200f, 200f);

        Assert.That(result.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(10f)));
    }

    [Test]
    public void ArmorTwoHundredHalvesPhysicalDamage()
    {
        var damage = CreateDamage(("Piercing", 20f));

        var result = ArmorRatingMath.Apply(damage, 200f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(ArmorRatingMath.ArmorScale, Is.EqualTo(200f));
            Assert.That(result.EffectiveArmor, Is.EqualTo(200f));
            Assert.That(result.DamageMultiplier, Is.EqualTo(0.5f));
            Assert.That(result.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(10f)));
            Assert.That(result.AbsorbedDamage, Is.EqualTo(10f));
        });
    }

    [Test]
    public void WeakPhysicalHitIsNotAutomaticallyBlocked()
    {
        var damage = CreateDamage(("Piercing", 5f), ("Heat", 3f));

        var result = ArmorRatingMath.Apply(damage, 800f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(result.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(1f)));
            Assert.That(result.Damage.DamageDict["Heat"], Is.EqualTo(FixedPoint2.New(3f)));
            Assert.That(result.AbsorbedDamage, Is.EqualTo(4f));
        });
    }

    [TestCase(10f, 5f)]
    [TestCase(100f, 50f)]
    [TestCase(500f, 250f)]
    public void ArmorReductionDoesNotDependOnHitSize(float incomingDamage, float expectedDamage)
    {
        var damage = CreateDamage(("Slash", incomingDamage));

        var result = ArmorRatingMath.Apply(damage, 200f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(result.DamageMultiplier, Is.EqualTo(0.5f));
            Assert.That(result.Damage.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(expectedDamage)));
        });
    }

    [TestCase(0f, 0f)]
    [TestCase(25f, 11.111f)]
    [TestCase(50f, 20f)]
    [TestCase(100f, 33.333f)]
    [TestCase(200f, 50f)]
    [TestCase(300f, 60f)]
    [TestCase(900f, 81.818f)]
    public void DisplayedPhysicalReductionMatchesCombatFormula(float armorRating, float expectedPercent)
    {
        Assert.That(ArmorRatingMath.GetDamageReductionPercent(armorRating),
            Is.EqualTo(expectedPercent).Within(0.001f));
    }

    [TestCase(100f, 0f, 33.333f)]
    [TestCase(100f, 50f, 20f)]
    [TestCase(100f, 100f, 0f)]
    [TestCase(100f, -100f, 50f)]
    public void DisplayedPhysicalReductionUsesFlatPenetration(
        float armorRating,
        float armorPenetration,
        float expectedPercent)
    {
        Assert.That(ArmorRatingMath.GetDamageReductionPercent(armorRating, armorPenetration),
            Is.EqualTo(expectedPercent).Within(0.001f));
    }

    [TestCase(0f, 100f)]
    [TestCase(25f, 88.889f)]
    [TestCase(50f, 80f)]
    [TestCase(100f, 66.667f)]
    [TestCase(200f, 50f)]
    [TestCase(300f, 40f)]
    [TestCase(900f, 18.182f)]
    public void CombatTableAtK200ProducesExpectedRemainingDamage(float armorRating, float expectedDamage)
    {
        var damage = CreateDamage(("Piercing", 100f));

        var result = ArmorRatingMath.Apply(damage, armorRating, 0f);

        Assert.That(result.Damage.DamageDict["Piercing"].Float(),
            Is.EqualTo(expectedDamage).Within(0.02f));
    }

    [TestCase(0f, 0f)]
    [TestCase(0.5f, 50f)]
    [TestCase(0.8f, 80f)]
    [TestCase(1f, 100f)]
    [TestCase(1.5f, 150f)]
    [TestCase(2f, 200f)]
    public void AllSupportedCoefficientsApplyDirectly(float coefficient, float expectedDamage)
    {
        var modifiers = new DamageModifierSet();
        modifiers.Coefficients["Slash"] = coefficient;
        var damage = CreateDamage(("Slash", 100f));

        var result = DamageSpecifier.ApplyModifierSet(damage, modifiers);

        if (coefficient == 0f)
        {
            Assert.That(result.DamageDict.ContainsKey("Slash"), Is.False);
            return;
        }

        Assert.That(result.DamageDict["Slash"].Float(), Is.EqualTo(expectedDamage).Within(0.02f));
    }

    [Test]
    public void TypeModifiersArePreservedAroundNumericalArmor()
    {
        var original = CreateDamage(("Piercing", 20f));
        var resistant = CreateDamage(("Piercing", 16f));
        var vulnerable = CreateDamage(("Piercing", 24f));
        var resistantEvent = new DamageModifyEvent(original);
        var vulnerableEvent = new DamageModifyEvent(original);

        resistantEvent.RecordArmorModifier(original, resistant);
        vulnerableEvent.RecordArmorModifier(original, vulnerable);

        var resistantResult = ArmorRatingMath.Apply(resistant, 200f, 0f);
        var vulnerableResult = ArmorRatingMath.Apply(vulnerable, 200f, 0f);
        var resistantRatingAbsorbed = resistantEvent.CalculateArmorRatingAbsorbedDamage(
            resistant,
            resistantResult.Damage);
        var vulnerableRatingAbsorbed = vulnerableEvent.CalculateArmorRatingAbsorbedDamage(
            vulnerable,
            vulnerableResult.Damage);

        Assert.Multiple(() =>
        {
            Assert.That(resistantResult.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(8f)));
            Assert.That(vulnerableResult.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(12f)));
            Assert.That(resistantEvent.CalculateArmorAbsorbedDamage(resistantRatingAbsorbed), Is.EqualTo(12f));
            Assert.That(vulnerableEvent.CalculateArmorAbsorbedDamage(vulnerableRatingAbsorbed), Is.EqualTo(12f));
        });
    }

    [Test]
    public void DurabilityWearOnlyCountsRecordedArmorChanges()
    {
        var original = CreateDamage(("Piercing", 20f), ("Heat", 10f));
        var afterUnrelatedResistance = CreateDamage(("Piercing", 10f), ("Heat", 10f));
        var afterArmor = CreateDamage(("Piercing", 10f), ("Heat", 5f));
        var ev = new DamageModifyEvent(original);

        ev.RecordArmorModifier(afterUnrelatedResistance, afterArmor);

        Assert.That(ev.CalculateArmorAbsorbedDamage(0f), Is.EqualTo(5f));
    }

    [Test]
    public void FlatAndPercentageProtectionAreCountedOnce()
    {
        var original = CreateDamage(("Piercing", 100f));
        var modifiers = new DamageModifierSet();
        modifiers.FlatReduction["Piercing"] = 10f;
        modifiers.Coefficients["Piercing"] = 0.5f;
        var afterModifiers = DamageSpecifier.ApplyModifierSet(original, modifiers);
        var ev = new DamageModifyEvent(original);

        ev.RecordArmorModifier(original, afterModifiers);
        var ratingResult = ArmorRatingMath.Apply(afterModifiers, 200f, 0f);
        var ratingAbsorbed = ev.CalculateArmorRatingAbsorbedDamage(afterModifiers, ratingResult.Damage);

        Assert.Multiple(() =>
        {
            Assert.That(afterModifiers.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(45f)));
            Assert.That(ratingResult.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(22.5f)));
            Assert.That(ev.CalculateArmorAbsorbedDamage(ratingAbsorbed), Is.EqualTo(77.5f));
        });
    }

    [Test]
    public void MixedPhysicalTypesUseSameRatingMultiplier()
    {
        var damage = CreateDamage(("Blunt", 10f), ("Piercing", 10f), ("Heat", 10f));

        var result = ArmorRatingMath.Apply(damage, 200f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(result.Damage.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(5f)));
            Assert.That(result.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(5f)));
            Assert.That(result.Damage.DamageDict["Heat"], Is.EqualTo(FixedPoint2.New(10f)));
        });
    }

    [Test]
    public void NumericalRatingDoesNotModifyNonPhysicalDamageOrHealing()
    {
        var damage = CreateDamage(("Piercing", 20f), ("Heat", 12f), ("Blunt", -5f));

        var result = ArmorRatingMath.Apply(damage, 200f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(result.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(10f)));
            Assert.That(result.Damage.DamageDict["Heat"], Is.EqualTo(FixedPoint2.New(12f)));
            Assert.That(result.Damage.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(-5f)));
        });
    }

    [Test]
    public void NumericalRatingDoesNotMutateIncomingDamage()
    {
        var damage = CreateDamage(("Piercing", 20f));

        _ = ArmorRatingMath.Apply(damage, 200f, 0f);

        Assert.That(damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(20f)));
    }

    [TestCase(0f, 200f, 10f)]
    [TestCase(100f, 100f, 13.33f)]
    [TestCase(200f, 0f, 20f)]
    [TestCase(300f, 0f, 20f)]
    [TestCase(-100f, 300f, 8f)]
    public void ArmorPenetrationChangesEffectiveRating(float penetration, float expectedArmor, float expectedDamage)
    {
        var damage = CreateDamage(("Piercing", 20f));

        var result = ArmorRatingMath.Apply(damage, 200f, penetration);

        Assert.Multiple(() =>
        {
            Assert.That(result.EffectiveArmor, Is.EqualTo(expectedArmor).Within(0.001f));
            Assert.That(result.Damage.DamageDict["Piercing"].Float(),
                Is.EqualTo(expectedDamage).Within(0.02f));
        });
    }

    [Test]
    public void InvalidPenetrationIsTreatedAsZero()
    {
        var damage = CreateDamage(("Piercing", 20f));

        var result = ArmorRatingMath.Apply(damage, 200f, float.NaN);

        Assert.Multiple(() =>
        {
            Assert.That(result.EffectiveArmor, Is.EqualTo(200f));
            Assert.That(result.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(10f)));
        });
    }

    [Test]
    public void ZeroRatingKeepsDamage()
    {
        var damage = CreateDamage(("Piercing", 0.5f));

        var result = ArmorRatingMath.Apply(damage, 0f, 0f);

        Assert.That(result.Damage.DamageDict["Piercing"], Is.EqualTo(FixedPoint2.New(0.5f)));
    }

    [Test]
    public void StrongestArmorWinsCandidateSelection()
    {
        var low = new EntityUid(1);
        var high = new EntityUid(2);
        var equallyHighButSpecific = new EntityUid(3);
        var ev = new DamageModifyEvent(CreateDamage(("Piercing", 20f)));

        ev.ConsiderArmorRatingCandidate(low, 40f, 1);
        ev.ConsiderArmorRatingCandidate(high, 60f, 10);
        ev.ConsiderArmorRatingCandidate(equallyHighButSpecific, 60f, 1);

        ev.ConsiderArmorDurabilityCandidate(low, 40f, 1, 0f);
        ev.ConsiderArmorDurabilityCandidate(high, 60f, 10, 0f);
        ev.ConsiderArmorDurabilityCandidate(equallyHighButSpecific, 60f, 1, 0f);
        ev.SelectArmorDurabilityCandidate(1f);

        Assert.Multiple(() =>
        {
            Assert.That(ev.ArmorRatingCandidate, Is.EqualTo(equallyHighButSpecific));
            Assert.That(ev.ArmorRating, Is.EqualTo(60f));
            Assert.That(ev.ArmorDurabilityCandidate, Is.EqualTo(equallyHighButSpecific));
        });
    }

    [Test]
    public void NonPhysicalHitWearsTheLayerWhoseModifierProtected()
    {
        var ratingArmor = new EntityUid(1);
        var resistanceArmor = new EntityUid(2);
        var original = CreateDamage(("Heat", 100f));
        var afterResistance = CreateDamage(("Heat", 50f));
        var ev = new DamageModifyEvent(original);

        var modifierAbsorbed = ev.RecordArmorModifier(original, afterResistance);
        ev.ConsiderArmorRatingCandidate(ratingArmor, 200f, 1);
        ev.ConsiderArmorDurabilityCandidate(ratingArmor, 200f, 1, 0f);
        ev.ConsiderArmorDurabilityCandidate(resistanceArmor, 0f, 1, modifierAbsorbed);
        var durabilityAbsorbed = ev.SelectArmorDurabilityCandidate(0f);

        Assert.Multiple(() =>
        {
            Assert.That(ev.ArmorDurabilityCandidate, Is.EqualTo(resistanceArmor));
            Assert.That(durabilityAbsorbed, Is.EqualTo(50f));
        });
    }

    [Test]
    public void PhysicalHitWearsTheLayerWhoseRatingProtected()
    {
        var ratingArmor = new EntityUid(1);
        var resistanceArmor = new EntityUid(2);
        var original = CreateDamage(("Slash", 100f));
        var afterResistance = CreateDamage(("Slash", 50f));
        var afterRating = CreateDamage(("Slash", 25f));
        var ev = new DamageModifyEvent(original);

        var modifierAbsorbed = ev.RecordArmorModifier(original, afterResistance);
        ev.ConsiderArmorRatingCandidate(ratingArmor, 200f, 1);
        ev.ConsiderArmorDurabilityCandidate(ratingArmor, 200f, 1, 0f);
        ev.ConsiderArmorDurabilityCandidate(resistanceArmor, 0f, 1, modifierAbsorbed);
        var ratingAbsorbed = ev.CalculateArmorRatingAbsorbedDamage(afterResistance, afterRating);
        var durabilityAbsorbed = ev.SelectArmorDurabilityCandidate(ratingAbsorbed);

        Assert.Multiple(() =>
        {
            Assert.That(ev.ArmorDurabilityCandidate, Is.EqualTo(ratingArmor));
            Assert.That(durabilityAbsorbed, Is.EqualTo(75f));
        });
    }

    [Test]
    public void BodyPartRelayCannotSelectDurabilityCandidate()
    {
        var armor = new EntityUid(1);
        var ev = new DamageModifyEvent(CreateDamage(("Slash", 100f)))
        {
            TrackArmorDurability = false,
        };

        ev.ConsiderArmorRatingCandidate(armor, 200f, 1);
        ev.ConsiderArmorDurabilityCandidate(armor, 200f, 1, 50f);

        Assert.Multiple(() =>
        {
            Assert.That(ev.SelectArmorDurabilityCandidate(50f), Is.Zero);
            Assert.That(ev.ArmorDurabilityCandidate, Is.Null);
        });
    }

    private static DamageSpecifier CreateDamage(params (string Type, float Amount)[] values)
    {
        var damage = new DamageSpecifier();
        foreach (var (type, amount) in values)
        {
            damage.DamageDict[type] = FixedPoint2.New(amount);
        }

        return damage;
    }
}
