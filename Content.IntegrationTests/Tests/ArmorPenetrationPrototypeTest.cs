using Content.Server.Projectiles;
using Content.Shared.CombatMode;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Projectiles;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class ArmorPenetrationPrototypeTest
{
    [TestPrototypes]
    private const string TestPrototypes = @"
- type: entity
  id: ArmorPenetrationStage7Armor
  components:
  - type: Item
  - type: Clothing
    slots: [outerClothing]
  - type: Armor
    armorRating: 200
    protectedBodyParts: All
    modifiers: {}

- type: entity
  id: ArmorPenetrationStage7Projectile
  parent: BaseBullet
  components:
  - type: Projectile
    damage:
      types:
        Piercing: 20
    armorPenetration: 100
    deleteOnCollide: false

- type: entity
  id: ArmorPenetrationStage7ArmArmor
  components:
  - type: Item
  - type: Clothing
    slots: [outerClothing]
  - type: Armor
    armorRating: 200
    modifiers: {}

- type: entity
  id: ArmorPenetrationStage7Hitscan
  components:
  - type: HitscanBasicDamage
    damage:
      types:
        Piercing: 20
    armorPenetration: 100

- type: entity
  id: ArmorPenetrationStage7Melee
  parent: BaseItem
  components:
  - type: MeleeWeapon
    damage:
      types:
        Slash: 20
    armorPenetration: 100
    range: 2
";

    [Test]
    public async Task CombatArmorPenetrationUsesFlatPoints()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();
        var projectileName = componentFactory.GetComponentName<ProjectileComponent>();
        var hitscanName = componentFactory.GetComponentName<HitscanBasicDamageComponent>();
        var meleeName = componentFactory.GetComponentName<MeleeWeaponComponent>();

        await server.WaitAssertion(() =>
        {
            foreach (var prototype in prototypeManager.EnumeratePrototypes<EntityPrototype>())
            {
                AssertFlatPoints(prototype, projectileName);
                AssertFlatPoints(prototype, hitscanName);
                AssertFlatPoints(prototype, meleeName);
            }

            AssertPrototypeValue(prototypeManager, projectileName, "Bullet9x19mmFMJ", 10f);
            AssertPrototypeValue(prototypeManager, projectileName, "Bullet9x19mmPlasteelAP", 180f);
            AssertPrototypeValue(prototypeManager, projectileName, "Bullet762x39mmHP", -130f);
            AssertPrototypeValue(prototypeManager, projectileName, "BulletHawk", 60f);
            AssertPrototypeValue(prototypeManager, hitscanName, "Coilgun134x92mm", 160f);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProjectileHitscanAndMeleePassFlatPenetrationIntoDamagePipeline()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var observation = server.System<ArmorPenetrationDamageObservationSystem>();

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            var projectileSystem = server.System<ProjectileSystem>();
            var meleeSystem = server.System<SharedMeleeWeaponSystem>();
            var combatMode = server.System<SharedCombatModeSystem>();

            var projectileTarget = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var projectileArmor = server.EntMan.SpawnEntity("ArmorPenetrationStage7Armor", testMap.GridCoords);
            Assert.That(inventory.TryEquip(projectileTarget, projectileArmor, "outerClothing", force: true), Is.True);
            var projectile = server.EntMan.SpawnEntity("ArmorPenetrationStage7Projectile", testMap.GridCoords);
            var projectileComponent = server.EntMan.GetComponent<ProjectileComponent>(projectile);
            var projectilePhysics = server.EntMan.GetComponent<PhysicsComponent>(projectile);
            var projectileResult = projectileSystem.ProjectileCollide(
                (projectile, projectileComponent, projectilePhysics), projectileTarget);

            Assert.That(projectileResult, Is.Not.Null);
            Assert.That(projectileResult!.DamageDict["Piercing"].Float(), Is.EqualTo(13.33f).Within(0.02f),
                "Projectile AP не дошёл до числовой брони");

            var hitscanTarget = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var hitscanArmor = server.EntMan.SpawnEntity("ArmorPenetrationStage7Armor", testMap.GridCoords);
            Assert.That(inventory.TryEquip(hitscanTarget, hitscanArmor, "outerClothing", force: true), Is.True);
            var hitscan = server.EntMan.SpawnEntity("ArmorPenetrationStage7Hitscan", testMap.GridCoords);
            observation.Reset();
            var hitscanEvent = new HitscanRaycastFiredEvent
            {
                HitEntity = hitscanTarget,
                Gun = hitscan,
            };
            server.EntMan.EventBus.RaiseLocalEvent(hitscan, ref hitscanEvent);

            Assert.That(observation.HasLastDamage, Is.True);
            Assert.That(observation.LastDamage.DamageDict["Piercing"].Float(), Is.EqualTo(13.33f).Within(0.02f),
                "Hitscan AP не дошёл до числовой брони");

            var meleeUser = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var meleeTarget = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var meleeArmor = server.EntMan.SpawnEntity("ArmorPenetrationStage7Armor", testMap.GridCoords);
            Assert.That(inventory.TryEquip(meleeTarget, meleeArmor, "outerClothing", force: true), Is.True);
            var melee = server.EntMan.SpawnEntity("ArmorPenetrationStage7Melee", testMap.GridCoords);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(meleeUser, melee), Is.True);
            var meleeComponent = server.EntMan.GetComponent<MeleeWeaponComponent>(melee);
            meleeComponent.NextAttack = TimeSpan.Zero;
            combatMode.SetInCombatMode(meleeUser, true);
            observation.Reset();

            Assert.That(meleeSystem.AttemptLightAttack(meleeUser, melee, meleeComponent, meleeTarget), Is.True);
            Assert.That(observation.HasLastDamage, Is.True);
            Assert.That(observation.LastDamage.DamageDict["Slash"].Float(), Is.EqualTo(13.33f).Within(0.02f),
                "Melee AP не дошёл до числовой брони");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LimbDamageKeepsHitLocationAndPenetration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            var body = server.System<SharedBodySystem>();
            var damageable = server.System<DamageableSystem>();
            var target = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var armor = server.EntMan.SpawnEntity("ArmorPenetrationStage7ArmArmor", testMap.GridCoords);
            Assert.That(inventory.TryEquip(target, armor, "outerClothing", force: true), Is.True);

            var leftArm = body.GetBodyChildrenOfType(target, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left)!.First().Id;
            var leftLeg = body.GetBodyChildrenOfType(target, BodyPartType.Leg, symmetry: BodyPartSymmetry.Left)!.First().Id;

            var armDamage = new DamageSpecifier();
            armDamage.DamageDict["Piercing"] = FixedPoint2.New(20f);
            damageable.TryChangeDamage(
                target,
                armDamage,
                armorPenetration: 150f,
                targetPart: TargetBodyPart.LeftArm);

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<DamageableComponent>(leftArm).TotalDamage.Float(),
                    Is.EqualTo(5.6f).Within(0.02f),
                    "Рука должна получить AP-ослабленную защиту верхней брони");
                Assert.That(server.EntMan.GetComponent<TargetingComponent>(target).LastDamagedPart,
                    Is.EqualTo(TargetBodyPart.LeftArm));
            });

            var woundDamage = new DamageSpecifier();
            woundDamage.DamageDict["Piercing"] = FixedPoint2.New(100f);
            damageable.TryChangeDamage(
                target,
                woundDamage,
                armorPenetration: 150f,
                targetPart: TargetBodyPart.LeftArm);

            Assert.That(body.GetBodyPartStatus(target)[TargetBodyPart.LeftArm],
                Is.EqualTo(TargetIntegrity.SomewhatWounded),
                "Анализатор должен получать степень повреждения руки с сервера");

            var legDamage = new DamageSpecifier();
            legDamage.DamageDict["Piercing"] = FixedPoint2.New(20f);
            damageable.TryChangeDamage(
                target,
                legDamage,
                armorPenetration: 150f,
                targetPart: TargetBodyPart.LeftLeg);

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<DamageableComponent>(leftLeg).TotalDamage.Float(),
                    Is.EqualTo(5.6f).Within(0.02f),
                    "Нога должна получить AP-ослабленную защиту верхней брони");
                Assert.That(server.EntMan.GetComponent<TargetingComponent>(target).LastDamagedPart,
                    Is.EqualTo(TargetBodyPart.LeftLeg));
            });
        });

        await pair.CleanReturnAsync();
    }

    private static void AssertFlatPoints(EntityPrototype prototype, string componentName)
    {
        if (!prototype.Components.TryGetComponent(componentName, out var registration))
            return;

        var armorPenetration = registration switch
        {
            ProjectileComponent projectile => projectile.ArmorPenetration,
            HitscanBasicDamageComponent hitscan => hitscan.ArmorPenetration,
            MeleeWeaponComponent melee => melee.ArmorPenetration,
            _ => 0f,
        };

        Assert.That(float.IsFinite(armorPenetration), Is.True, prototype.ID);
        Assert.That(MathF.Abs(armorPenetration - MathF.Round(armorPenetration)), Is.LessThan(0.001f),
            $"{prototype.ID} still has fractional armor penetration: {armorPenetration}");
    }

    private static void AssertPrototypeValue(
        IPrototypeManager prototypeManager,
        string componentName,
        string prototypeId,
        float expected)
    {
        var prototype = prototypeManager.Index<EntityPrototype>(prototypeId);
        Assert.That(prototype.Components.TryGetComponent(componentName, out var registration), Is.True, prototypeId);

        var actual = registration switch
        {
            ProjectileComponent projectile => projectile.ArmorPenetration,
            HitscanBasicDamageComponent hitscan => hitscan.ArmorPenetration,
            MeleeWeaponComponent melee => melee.ArmorPenetration,
            _ => float.NaN,
        };

        Assert.That(actual, Is.EqualTo(expected), prototypeId);
    }
}

public sealed class ArmorPenetrationDamageObservationSystem : EntitySystem
{
    public DamageSpecifier LastDamage { get; private set; } = new();
    public bool HasLastDamage { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageChanged);
    }

    public void Reset()
    {
        LastDamage = new DamageSpecifier();
        HasLastDamage = false;
    }

    private void OnDamageChanged(EntityUid uid, DamageableComponent component, DamageChangedEvent args)
    {
        if (args.DamageDelta is { } delta && args.DamageIncreased)
        {
            LastDamage = delta;
            HasLastDamage = true;
        }
    }
}
