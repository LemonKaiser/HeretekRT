using Content.Server._NF.Shipyard.Systems;
using Content.Shared._Mono.Economy;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class ShipyardCashPaymentTest
{
    [TestPrototypes]
    public const string Prototypes = """
        - type: entity
          id: TestShipyardCashReceiver
          components:
          - type: ItemSlots
          - type: CreditReceiver

        - type: entity
          id: TestShipyardCashBuyer
          components:
          - type: BankAccount
        """;

    [TestCase(75, false, true, 25)]
    [TestCase(125, false, false, 100)]
    [TestCase(125, true, false, 100)]
    [TestCase(100, true, true, 0)]
    [TestCase(0, false, false, 100)]
    public async Task CashPaymentPreservesMoneyOnRejectedPurchase(int price, bool ironman, bool success, int remainingCash)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var receiver = entities.SpawnEntity("TestShipyardCashReceiver", map.GridCoords);
            var buyer = entities.SpawnEntity("TestShipyardCashBuyer", map.GridCoords);
            if (ironman)
                entities.AddComponent<IronmanComponent>(buyer);
            var initialBankBalance = entities.GetComponent<BankAccountComponent>(buyer).Balance;

            var cash = entities.SpawnEntity("SpaceCash100", map.GridCoords);
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(receiver, "cash_slot", cash, null), Is.True);

            Assert.That(server.System<ShipyardSystem>().TryPayForShuttle(receiver, buyer, price), Is.EqualTo(success));
            Assert.That(entities.GetComponent<StackComponent>(cash).Count, Is.EqualTo(remainingCash));
            Assert.That(server.System<SharedCreditReceiverSystem>().GetCashBalance(receiver), Is.EqualTo(remainingCash));
            Assert.That(entities.GetComponent<BankAccountComponent>(buyer).Balance, Is.EqualTo(initialBankBalance));
        });

        await pair.CleanReturnAsync();
    }
}
