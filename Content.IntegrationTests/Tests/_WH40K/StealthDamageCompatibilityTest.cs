using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Stealth;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class StealthDamageCompatibilityTest
{
    [TestPrototypes]
    public const string Prototypes = """
        - type: entity
          id: TestDamageReveal
          components:
          - type: Damageable
          - type: Stealth
          - type: RevealOnDamage
        """;

    [TestCase("Blunt", true)]
    [TestCase("Poison", false)]
    public async Task DamageOnlyRevealsForConfiguredTypes(string type, bool reveal)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var target = server.EntMan.SpawnEntity("TestDamageReveal", map.GridCoords);
            var stealth = server.System<SharedStealthSystem>();
            stealth.SetVisibility(target, -1.5f);
            var damage = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>(type), 10);
            server.System<DamageableSystem>().TryChangeDamage(target, damage, true);
            Assert.That(server.EntMan.GetComponent<DamageableComponent>(target).TotalDamage, Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(stealth.GetVisibility(target) > 0, Is.EqualTo(reveal));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HealingDoesNotRevealStealth()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var target = server.EntMan.SpawnEntity("TestDamageReveal", map.GridCoords);
            var type = "Blunt";
            var damage = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>(type), 10);
            var damageSystem = server.System<DamageableSystem>();
            damageSystem.TryChangeDamage(target, damage, true);
            var stealth = server.System<SharedStealthSystem>();
            stealth.SetVisibility(target, -1.5f);
            damageSystem.TryChangeDamage(target, damage * -0.5f, true);
            Assert.That(server.EntMan.GetComponent<DamageableComponent>(target).TotalDamage, Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(stealth.GetVisibility(target), Is.EqualTo(-1.5f));
        });
        await pair.CleanReturnAsync();
    }
}
