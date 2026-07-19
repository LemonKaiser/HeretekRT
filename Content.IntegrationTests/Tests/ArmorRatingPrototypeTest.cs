using System.Collections.Generic;
using Content.Server.Armor;
using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Content.Shared._Shitmed.Targeting;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class ArmorRatingPrototypeTest
{
    [TestPrototypes]
    private const string TestPrototypes = @"
- type: entity
  id: ArmorRatingLegacyRuntimeTest
  abstract: true
  components:
  - type: Armor
    modifiers:
      coefficients:
        Blunt: 0.65
        Heat: 0.5
        Radiation: 0.0
        Shock: 1.5

- type: entity
  parent: ArmorRatingLegacyRuntimeTest
  id: ArmorRatingLegacyRuntimeChildTest

- type: entity
  id: ArmorRatingConfiguredRuntimeTest
  components:
  - type: Armor
    armorRating: 70
    modifiers:
      coefficients:
        Blunt: 0.2

- type: entity
  id: ArmorRatingSpecialOnlyRuntimeTest
  components:
  - type: Armor
    modifiers:
      coefficients:
        Radiation: 0.0
        Caustic: 0.25

- type: entity
  id: ArmorDetailedExamineRuntimeTest
  components:
  - type: Armor
    armorRating: 200
    protectedBodyParts: Head
    modifiers:
      coefficients:
        Blunt: 0.5
        Heat: 1.5
        Radiation: 0.0
      flatReductions:
        Caustic: 5

- type: entity
  id: ArmorExamineFormattingRuntimeTest
  components:
  - type: Armor
    armorRating: 60
    protectedBodyParts: Torso
    modifiers:
      coefficients:
        Blunt: 0.876
        Heat: 1.234
      flatReductions:
        Caustic: 5.678

- type: entity
  id: ArmorStage7CoverageRuntimeTest
  components:
  - type: Armor
    armorRating: 200
    protectedBodyParts: All
    modifiers: {}

- type: entity
  id: ArmorStage7RatingLowRuntimeTest
  components:
  - type: Armor
    armorRating: 100
    protectedBodyParts: Torso
    modifiers:
      coefficients:
        Slash: 0.8
        Radiation: 0.0

- type: entity
  id: ArmorStage7RatingHighRuntimeTest
  components:
  - type: Armor
    armorRating: 300
    protectedBodyParts: Torso
    modifiers:
      coefficients:
        Slash: 0.5
        Radiation: 0.0
";

    [Test]
    public async Task Wh40kArmorRatingsMatchTheirProtectionClasses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();
        var armorName = componentFactory.GetComponentName<ArmorComponent>();
        var expectedRatings = new Dictionary<string, float>
        {
            // Flak and carapace body armor.
            ["ClothingOuterArmorFlakVestOld"] = 48f,
            ["ClothingOuterArmorFlakVestLight"] = 80f,
            ["ClothingOuterArmorFlakVest"] = 100f,
            ["ClothingOuterArmorFlakVestMed"] = 100f,
            ["ClothingOuterArmorAdvancedFlakVest"] = 240f,
            ["ClothingOuterArmorFlakVestGen"] = 280f,
            ["ClothingOuterArmorHeresiarchCardinal"] = 340f,

            // Specialist and sealed body armor.
            ["ClothingOuterArmorCommissar"] = 220f,
            ["ClothingOuterArmorJuniorOfficerArmoredJacket"] = 72f,
            ["ClothingOuterHardsuitVoidsmanStandard"] = 160f,
            ["ClothingOuterHardsuitVoidsmanOfficer"] = 280f,
            ["ClothingOuterHardsuitTechpriestMarsSkitarii"] = 280f,
            ["ClothingOuterArmorArd"] = 300f,

            // Power armor and its inherited variants.
            ["ClothingOuterArmorAstartesMk7"] = 480f,
            ["ClothingOuterArmorAstartesMk2"] = 480f,
            ["ClothingOuterArmorAstartesMk7BlackTemplars"] = 480f,

            // Head and extremity armor.
            ["ClothingHeadHelmetFlak"] = 48f,
            ["ClothingHeadHelmetFlakAdv"] = 80f,
            ["ClothingHeadHelmetHardsuitTechpriest"] = 72f,
            ["ClothingHeadHelmetHardsuitTechpriestMarsMagos"] = 100f,
            ["ClothingHeadHelmetAstartesMk7"] = 320f,
            ["ClothingHeadHelmetAstartesMk2"] = 320f,
            ["ClothingShoesArmoredWorkbootsTechpriest"] = 20f,
        };

        await server.WaitAssertion(() =>
        {
            foreach (var prototype in prototypeManager.EnumeratePrototypes<EntityPrototype>())
            {
                if (!prototype.Components.TryGetComponent(armorName, out var registration))
                    continue;

                var armor = (ArmorComponent) registration;
                Assert.That(armor.ArmorRating, Is.GreaterThanOrEqualTo(0f), prototype.ID);
            }

            foreach (var (prototypeId, expectedRating) in expectedRatings)
            {
                var prototype = prototypeManager.Index<EntityPrototype>(prototypeId);
                Assert.That(prototype.Components.TryGetComponent(armorName, out var registration), Is.True, prototypeId);
                var armor = (ArmorComponent) registration!;
                Assert.That(armor.ArmorRating, Is.EqualTo(expectedRating), prototypeId);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryArmorPrototypeHasFiniteRatingAndCoefficients()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();
        var armorName = componentFactory.GetComponentName<ArmorComponent>();

        await server.WaitAssertion(() =>
        {
            foreach (var prototype in prototypeManager.EnumeratePrototypes<EntityPrototype>())
            {
                if (!prototype.Components.TryGetComponent(armorName, out var registration))
                    continue;

                var armor = (ArmorComponent) registration;
                Assert.That(float.IsFinite(armor.ArmorRating) && armor.ArmorRating >= 0f,
                    Is.True, prototype.ID);
                Assert.That(MathF.Abs(armor.ArmorRating - MathF.Round(armor.ArmorRating)), Is.LessThan(0.001f),
                    $"{prototype.ID} has a fractional armor rating: {armor.ArmorRating}");

                foreach (var (damageType, coefficient) in armor.Modifiers.Coefficients)
                {
                    Assert.That(float.IsFinite(coefficient), Is.True,
                        $"{prototype.ID} has an invalid {damageType} coefficient: {coefficient}");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArmorRatingMigrationCoversAllProtectionFamilies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();
        var armorName = componentFactory.GetComponentName<ArmorComponent>();
        var expectedRatings = new Dictionary<string, float>
        {
            // Base content.
            ["ClothingOuterArmorBasic"] = 320f,
            ["ClothingOuterArmorRiot"] = 480f,
            ["ClothingOuterHardsuitJuggernaut"] = 640f,
            ["ClothingOuterSuitRad"] = 0f,

            // Nyanotrasen, Goobstation and NF content.
            ["ClothingOuterArmorGladiator"] = 160f,
            ["ClothingOuterHardsuitCybersunStealth"] = 240f,
            ["ClothingOuterArmorBPVestHeavy"] = 600f,
            ["ClothingOuterEVASuitCaptain"] = 40f,

            // WH40K values use the same global fourfold rating scale.
            ["ClothingOuterArmorAstartesMk7"] = 480f,
        };

        await server.WaitAssertion(() =>
        {
            foreach (var (prototypeId, expectedRating) in expectedRatings)
            {
                var prototype = prototypeManager.Index<EntityPrototype>(prototypeId);
                Assert.That(prototype.Components.TryGetComponent(armorName, out var registration), Is.True, prototypeId);
                var armor = (ArmorComponent) registration!;
                Assert.That(armor.ArmorRating, Is.EqualTo(expectedRating), prototypeId);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArmorWithoutConfiguredRatingKeepsPercentagesAndNoNumericRating()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var uid = server.EntMan.SpawnEntity("ArmorRatingLegacyRuntimeChildTest", MapCoordinates.Nullspace);
            var armor = server.EntMan.GetComponent<ArmorComponent>(uid);

            Assert.That(armor.ArmorRating, Is.Zero);

            var damage = new DamageSpecifier();
            damage.DamageDict["Blunt"] = FixedPoint2.New(20f);
            damage.DamageDict["Heat"] = FixedPoint2.New(10f);
            damage.DamageDict["Radiation"] = FixedPoint2.New(4f);
            damage.DamageDict["Shock"] = FixedPoint2.New(10f);
            var modify = new DamageModifyEvent(damage);
            var relayed = new InventoryRelayedEvent<DamageModifyEvent>(modify);

            server.EntMan.EventBus.RaiseLocalEvent(uid, relayed);

            Assert.Multiple(() =>
            {
                Assert.That(modify.ArmorRatingCandidate, Is.Null);
                Assert.That(modify.ArmorRating, Is.Zero);
                Assert.That(modify.Damage.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(13f)));
                Assert.That(modify.Damage.DamageDict["Heat"], Is.EqualTo(FixedPoint2.New(5f)));
                Assert.That(modify.Damage.DamageDict.ContainsKey("Radiation"), Is.False);
                Assert.That(modify.Damage.DamageDict["Shock"], Is.EqualTo(FixedPoint2.New(15f)));
            });

            var result = ArmorRatingMath.Apply(modify.Damage, modify.ArmorRating, 0f);
            Assert.That(result.Damage.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(13f)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConfiguredAndSpecialOnlyArmorKeepTheirIntendedRatings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var configuredUid = server.EntMan.SpawnEntity("ArmorRatingConfiguredRuntimeTest", MapCoordinates.Nullspace);
            var configured = server.EntMan.GetComponent<ArmorComponent>(configuredUid);
            Assert.That(configured.ArmorRating, Is.EqualTo(70f));

            var configuredDamage = new DamageSpecifier();
            configuredDamage.DamageDict["Blunt"] = FixedPoint2.New(20f);
            var configuredModify = new DamageModifyEvent(configuredDamage);
            server.EntMan.EventBus.RaiseLocalEvent(configuredUid,
                new InventoryRelayedEvent<DamageModifyEvent>(configuredModify));

            var specialUid = server.EntMan.SpawnEntity("ArmorRatingSpecialOnlyRuntimeTest", MapCoordinates.Nullspace);
            var special = server.EntMan.GetComponent<ArmorComponent>(specialUid);
            var specialDamage = new DamageSpecifier();
            specialDamage.DamageDict["Radiation"] = FixedPoint2.New(4f);
            specialDamage.DamageDict["Caustic"] = FixedPoint2.New(10f);
            var specialModify = new DamageModifyEvent(specialDamage);
            server.EntMan.EventBus.RaiseLocalEvent(specialUid,
                new InventoryRelayedEvent<DamageModifyEvent>(specialModify));

            Assert.Multiple(() =>
            {
                Assert.That(configuredModify.ArmorRating, Is.EqualTo(70f));
                Assert.That(configuredModify.Damage.DamageDict["Blunt"], Is.EqualTo(FixedPoint2.New(4f)));
                Assert.That(special.ArmorRating, Is.Zero);
                Assert.That(specialModify.Damage.DamageDict.ContainsKey("Radiation"), Is.False);
                Assert.That(specialModify.Damage.DamageDict["Caustic"], Is.EqualTo(FixedPoint2.New(2.5f)));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArmorCoverageHandlesAllBodyPartFamilies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var uid = server.EntMan.SpawnEntity("ArmorStage7CoverageRuntimeTest", MapCoordinates.Nullspace);
            var armor = server.EntMan.GetComponent<ArmorComponent>(uid);
            var cases = new[]
            {
                (TargetBodyPart.Head, TargetBodyPart.Head),
                (TargetBodyPart.Torso, TargetBodyPart.Torso),
                (TargetBodyPart.Arms, TargetBodyPart.LeftArm),
                (TargetBodyPart.Legs, TargetBodyPart.RightLeg),
                (TargetBodyPart.Hands, TargetBodyPart.LeftHand),
                (TargetBodyPart.Feet, TargetBodyPart.RightFoot),
            };

            foreach (var (coverage, targetPart) in cases)
            {
                armor.ProtectedBodyParts = coverage;
                var damage = new DamageSpecifier();
                damage.DamageDict["Slash"] = FixedPoint2.New(20f);
                var modify = new DamageModifyEvent(damage, targetPart: targetPart);

                server.EntMan.EventBus.RaiseLocalEvent(uid,
                    new InventoryRelayedEvent<DamageModifyEvent>(modify));

                var result = ArmorRatingMath.Apply(modify.Damage, modify.ArmorRating, 0f);

                Assert.That(modify.ArmorRating, Is.EqualTo(200f),
                    $"Coverage {coverage} did not protect {targetPart}");
                Assert.That(result.Damage.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(10f)),
                    $"Coverage {coverage} produced wrong damage for {targetPart}");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public void ClothingSlotCoverageMatchesBodyProtectionRules()
    {
        var outerCoverage = ArmorCoverage.FromSlots(SlotFlags.OUTERCLOTHING);

        Assert.Multiple(() =>
        {
            Assert.That(outerCoverage, Is.EqualTo(TargetBodyPart.Torso | TargetBodyPart.Arms | TargetBodyPart.Legs));
            Assert.That(ArmorCoverage.Covers(outerCoverage, TargetBodyPart.LeftArm), Is.True);
            Assert.That(ArmorCoverage.Covers(outerCoverage, TargetBodyPart.RightLeg), Is.True);
            Assert.That(ArmorCoverage.FromSlots(SlotFlags.GLOVES), Is.EqualTo(TargetBodyPart.Hands));
            Assert.That(ArmorCoverage.FromSlots(SlotFlags.LEGS), Is.EqualTo(TargetBodyPart.Legs));
        });
    }

    [Test]
    public async Task StrongestRatingDoesNotStackAndPercentagesRemainMultiplicative()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var lowUid = server.EntMan.SpawnEntity("ArmorStage7RatingLowRuntimeTest", MapCoordinates.Nullspace);
            var highUid = server.EntMan.SpawnEntity("ArmorStage7RatingHighRuntimeTest", MapCoordinates.Nullspace);
            var damage = new DamageSpecifier();
            damage.DamageDict["Slash"] = FixedPoint2.New(100f);
            damage.DamageDict["Radiation"] = FixedPoint2.New(10f);
            var modify = new DamageModifyEvent(damage, targetPart: TargetBodyPart.Torso);

            server.EntMan.EventBus.RaiseLocalEvent(lowUid,
                new InventoryRelayedEvent<DamageModifyEvent>(modify));
            server.EntMan.EventBus.RaiseLocalEvent(highUid,
                new InventoryRelayedEvent<DamageModifyEvent>(modify));

            var afterArmorPenetration = ArmorRatingMath.Apply(modify.Damage, modify.ArmorRating, 300f);

            Assert.Multiple(() =>
            {
                Assert.That(modify.ArmorRating, Is.EqualTo(300f),
                    "Числовые рейтинги экипировки не должны складываться");
                Assert.That(modify.Damage.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(40f)),
                    "Процентные коэффициенты должны перемножаться");
                Assert.That(modify.Damage.DamageDict.ContainsKey("Radiation"), Is.False,
                    "Иммунитет должен сохраниться после процентного слоя");
                Assert.That(afterArmorPenetration.Damage.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(40f)),
                    "AP не должен ослаблять процентную защиту");
                Assert.That(afterArmorPenetration.Damage.DamageDict.ContainsKey("Radiation"), Is.False,
                    "AP не должен отменять иммунитет");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DetailedExamineExplainsArmorResistanceVulnerabilityAndPenetration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var localization = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var uid = server.EntMan.SpawnEntity("ArmorDetailedExamineRuntimeTest", MapCoordinates.Nullspace);
            var armor = server.EntMan.GetComponent<ArmorComponent>(uid);
            var armorMarkup = server.System<ArmorSystem>().GetArmorExamine(uid, armor).ToMarkup();

            var blunt = localization.GetString("armor-damage-type-blunt");
            var heat = localization.GetString("armor-damage-type-heat");
            var radiation = localization.GetString("armor-damage-type-radiation");
            var caustic = localization.GetString("armor-damage-type-caustic");
            var head = localization.GetString("armor-coverage-part-head");

            Assert.Multiple(() =>
            {
                Assert.That(armorMarkup, Does.Contain(NormalizeMarkup(
                    localization.GetString("armor-rating-value", ("value", 200f)))));
                Assert.That(armorMarkup, Does.Contain(NormalizeMarkup(
                    localization.GetString("armor-rating-protection-value", ("value", 50f)))));
                Assert.That(armorMarkup, Does.Contain(NormalizeMarkup(
                    localization.GetString("armor-coverage-summary", ("parts", head)))));
                Assert.That(armorMarkup, Does.Contain(NormalizeMarkup(localization.GetString("armor-resistance-value",
                    ("type", blunt), ("value", 50f)))));
                Assert.That(armorMarkup, Does.Contain(NormalizeMarkup(localization.GetString("armor-vulnerability-value",
                    ("type", heat), ("value", 50f)))));
                Assert.That(armorMarkup, Does.Contain(NormalizeMarkup(
                    localization.GetString("armor-immunity-value", ("type", radiation)))));
                Assert.That(armorMarkup, Does.Contain(NormalizeMarkup(localization.GetString("armor-reduction-value",
                    ("type", caustic), ("value", 5f)))));
            });

            var damageExamine = server.System<DamageExamineSystem>();
            var positivePenetration = new FormattedMessage();
            var negativePenetration = new FormattedMessage();
            var noPenetration = new FormattedMessage();
            damageExamine.AddArmorPenetrationExamine(positivePenetration, 100f);
            damageExamine.AddArmorPenetrationExamine(negativePenetration, -100f);
            damageExamine.AddArmorPenetrationExamine(noPenetration, 0f);

            Assert.Multiple(() =>
            {
                Assert.That(positivePenetration.ToMarkup(),
                    Does.Contain(NormalizeMarkup(localization.GetString("damage-armor-penetration", ("amount", 100f)))));
                Assert.That(negativePenetration.ToMarkup(),
                    Does.Contain(NormalizeMarkup(localization.GetString("damage-armor-penetration-penalty", ("amount", 100f)))));
                Assert.That(noPenetration.ToMarkup(),
                    Does.Contain(NormalizeMarkup(localization.GetString("damage-armor-penetration-none"))));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DetailedExamineRoundsNumbersToTenths()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var localization = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var uid = server.EntMan.SpawnEntity("ArmorExamineFormattingRuntimeTest", MapCoordinates.Nullspace);
            var armor = server.EntMan.GetComponent<ArmorComponent>(uid);
            var markup = server.System<ArmorSystem>().GetArmorExamine(uid, armor).ToMarkup();

            Assert.Multiple(() =>
            {
                Assert.That(markup, Does.Contain(NormalizeMarkup(localization.GetString(
                    "armor-rating-value", ("value", "60")))));
                Assert.That(markup, Does.Contain(NormalizeMarkup(localization.GetString(
                    "armor-rating-protection-value", ("value", "23.1")))));
                Assert.That(markup, Does.Contain(NormalizeMarkup(localization.GetString(
                    "armor-resistance-value", ("type", localization.GetString("armor-damage-type-blunt")),
                    ("value", "12.4")))));
                Assert.That(markup, Does.Contain(NormalizeMarkup(localization.GetString(
                    "armor-vulnerability-value", ("type", localization.GetString("armor-damage-type-heat")),
                    ("value", "23.4")))));
                Assert.That(markup, Does.Contain(NormalizeMarkup(localization.GetString(
                    "armor-reduction-value", ("type", localization.GetString("armor-damage-type-caustic")),
                    ("value", "5.7")))));
                Assert.That(markup, Does.Not.Contain("000000"));
            });

            var damageExamine = server.System<DamageExamineSystem>();
            var penetration = new FormattedMessage();
            damageExamine.AddArmorPenetrationExamine(penetration, 12.34f);
            Assert.That(penetration.ToMarkup(), Does.Contain(NormalizeMarkup(localization.GetString(
                "damage-armor-penetration", ("amount", "12.3")))));
        });

        await pair.CleanReturnAsync();
    }

    private static string NormalizeMarkup(string markup)
    {
        return FormattedMessage.FromMarkupOrThrow(markup).ToMarkup();
    }
}
