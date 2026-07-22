using Content.Shared.Armor;
using Content.Shared.Inventory;
using Content.Shared._Shitmed.Targeting;
using NUnit.Framework;

namespace Content.Tests.Shared.Armor;

[TestFixture]
public sealed class ArmorCoverageTest
{
    [Test]
    public void ClothingSlotsResolveToExpectedParts()
    {
        Assert.That(ArmorCoverage.FromSlots(SlotFlags.HEAD), Is.EqualTo(TargetBodyPart.Head));
        Assert.That(ArmorCoverage.FromSlots(SlotFlags.MASK), Is.EqualTo(TargetBodyPart.Head));
        Assert.That(ArmorCoverage.FromSlots(SlotFlags.OUTERCLOTHING),
            Is.EqualTo(TargetBodyPart.Torso | TargetBodyPart.Arms | TargetBodyPart.Legs));
        Assert.That(ArmorCoverage.FromSlots(SlotFlags.GLOVES), Is.EqualTo(TargetBodyPart.Hands));
        Assert.That(ArmorCoverage.FromSlots(SlotFlags.FEET), Is.EqualTo(TargetBodyPart.Feet));
    }

    [Test]
    public void CombinedClothingSlotsResolveToCombinedParts()
    {
        var coverage = ArmorCoverage.FromSlots(SlotFlags.HEAD | SlotFlags.GLOVES);

        Assert.That(coverage, Is.EqualTo(TargetBodyPart.Head | TargetBodyPart.Hands));
    }

    [Test]
    public void CoverageMatchesSpecificAndBroadTargets()
    {
        var coverage = TargetBodyPart.Head | TargetBodyPart.Torso;

        Assert.Multiple(() =>
        {
            Assert.That(ArmorCoverage.Covers(coverage, TargetBodyPart.Head), Is.True);
            Assert.That(ArmorCoverage.Covers(coverage, TargetBodyPart.Torso), Is.True);
            Assert.That(ArmorCoverage.Covers(coverage, TargetBodyPart.LeftArm), Is.False);
            Assert.That(ArmorCoverage.Covers(coverage, TargetBodyPart.All), Is.True);
            Assert.That(ArmorCoverage.Covers(coverage, null), Is.True);
        });
    }

    [Test]
    public void EmptyCoverageNeverProtectsSpecificPart()
    {
        Assert.That(ArmorCoverage.Covers(0, TargetBodyPart.Head), Is.False);
    }
}
