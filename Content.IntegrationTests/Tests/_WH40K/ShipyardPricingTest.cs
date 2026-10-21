using System.Linq;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Server._NF.ShuttleRecords;
using Content.Server._NF.SectorServices;
using Content.Server._WH40K.Shipyard;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.BUI;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Cargo.Components;
using Content.Shared.Preferences;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class ShipyardPricingTest
{
    private static readonly ProtoId<VesselPrototype> VesselId = "Aquila";

    [Test]
    public async Task EquipmentChangesRaiseQuotesWithoutChangingYaml()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var maps = entities.System<MapSystem>();
        var pricing = entities.System<ShipyardPricingSystem>();

        await server.WaitAssertion(() =>
        {
            var vessel = prototypes.Index(VesselId);
            var originalPrice = vessel.Price;
            Assert.That(pricing.TryGetPrice(vessel, out var templatePrice), Is.True);
            Assert.That(templatePrice, Is.AtLeast(originalPrice));
            maps.CreateMap(out var mapId);
            try
            {
                var grid = maps.CreateGridEntity(mapId);
                maps.SetTile(grid, Vector2i.Zero, new Tile(1));
                var item = entities.SpawnEntity(null, new EntityCoordinates(grid, 0.5f, 0.5f));
                var itemPrice = entities.EnsureComponent<StaticPriceComponent>(item);

                Assert.That(pricing.TryGetPrice(vessel, grid, out var emptyPrice), Is.True);
                Assert.That(emptyPrice, Is.EqualTo(templatePrice));

                itemPrice.Price = templatePrice * 2 + 0.25;
                Assert.That(pricing.TryGetPrice(vessel, grid, out var equippedPrice), Is.True);
                Assert.That(equippedPrice, Is.GreaterThanOrEqualTo(itemPrice.Price));
                Assert.That(pricing.TryGetPrice(vessel, out var displayedPrice), Is.True);
                Assert.That(displayedPrice, Is.EqualTo(equippedPrice));

                itemPrice.Price *= 2;
                Assert.That(pricing.TryGetPrice(vessel, grid, out var raisedPrice), Is.True);
                Assert.That(raisedPrice, Is.GreaterThan(equippedPrice));
                Assert.That(pricing.TryGetPrice(vessel, out displayedPrice), Is.True);
                Assert.That(displayedPrice, Is.EqualTo(raisedPrice));

                itemPrice.Price = 0;
                Assert.That(pricing.TryGetPrice(vessel, grid, out var cheaperLoadoutPrice), Is.True);
                Assert.That(cheaperLoadoutPrice, Is.EqualTo(raisedPrice), "An existing quote must stay valid for a cheaper random loadout.");

                itemPrice.Price = (double) int.MaxValue + 1;
                Assert.That(pricing.TryGetPrice(vessel, grid, out _), Is.False, "Unrepresentable prices must not wrap around.");
                Assert.That(vessel.Price, Is.EqualTo(originalPrice), "Pricing must not mutate the YAML prototype.");
            }
            finally
            {
                maps.DeleteMap(mapId);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PurchaseUsesDisplayedPriceAndRejectsStaleQuotes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<MapSystem>();
        var pricing = entities.System<ShipyardPricingSystem>();
        var ui = entities.System<UserInterfaceSystem>();
        var stations = entities.System<StationSystem>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                var grid = maps.CreateGridEntity(mapId);
                maps.SetTile(grid, Vector2i.Zero, new Tile(1));
                entities.EnsureComponent<StationDataComponent>(station);
                entities.EnsureComponent<StationRecordsComponent>(station);
                entities.EnsureComponent<StationSectorServiceHostComponent>(station);
                stations.AddGridToStation(station, grid);
                var coordinates = new EntityCoordinates(grid, 0.5f, 0.5f);
                var console = entities.SpawnEntity("ComputerShipyard", coordinates);
                var listing = entities.GetComponent<ShipyardListingComponent>(console);
                listing.Shuttles.Clear();
                listing.Shuttles.Add(VesselId.Id);
                var component = entities.GetComponent<ShipyardConsoleComponent>(console);
                var id = entities.SpawnEntity("PassengerIDCard", coordinates);
                entities.System<SharedContainerSystem>().Insert(id, component.TargetIdSlot.ContainerSlot!);
                var player = entities.SpawnEntity(null, coordinates);
                var bank = entities.EnsureComponent<BankAccountComponent>(player);
                var vessel = prototypes.Index(VesselId);
                Assert.That(pricing.TryGetPrice(vessel, out var templatePrice), Is.True);
                var equipment = entities.SpawnEntity(null, coordinates);
                entities.EnsureComponent<StaticPriceComponent>(equipment).Price = templatePrice * 2 + 0.25;
                Assert.That(pricing.TryGetPrice(vessel, grid, out var expectedPrice), Is.True);
                Assert.That(expectedPrice, Is.GreaterThan(vessel.Price));

                var session = pair.Player!;
                var prefs = preferences.GetPreferences(session.UserId);
                var profile = (HumanoidCharacterProfile) prefs.SelectedCharacter;
                var balance = expectedPrice + 123;
                preferences.SetProfile(session.UserId, prefs.SelectedCharacterIndex, profile.WithBankBalance(balance)).Wait();
                server.PlayerMan.SetAttachedEntity(session, player);
                Assert.That(bank.Balance, Is.EqualTo(balance));
                var gridCount = entities.EntityQuery<MapGridComponent>().Count();

                entities.EventBus.RaiseLocalEvent(console, new ShipyardConsolePurchaseMessage(vessel.ID, 0)
                {
                    Actor = player,
                    UiKey = ShipyardConsoleUiKey.Shipyard,
                });

                Assert.That(ui.TryGetUiState<ShipyardConsoleInterfaceState>(console, ShipyardConsoleUiKey.Shipyard, out var state), Is.True);
                Assert.That(state!.VesselPrices[vessel.ID], Is.EqualTo(expectedPrice));
                Assert.That(bank.Balance, Is.EqualTo(balance));
                Assert.That(entities.HasComponent<ShuttleDeedComponent>(id), Is.False);
                Assert.That(entities.EntityQuery<MapGridComponent>().Count(), Is.EqualTo(gridCount));

                entities.EventBus.RaiseLocalEvent(console, new ShipyardConsolePurchaseMessage(vessel.ID, state.VesselPrices[vessel.ID])
                {
                    Actor = player,
                    UiKey = ShipyardConsoleUiKey.Shipyard,
                });

                Assert.That(bank.Balance, Is.EqualTo(123), "The server must charge the price shown in the UI.");
                Assert.That(entities.TryGetComponent<ShuttleDeedComponent>(id, out var deed), Is.True);
                Assert.That(entities.System<ShuttleRecordsSystem>().TryGetRecord(entities.GetNetEntity(deed!.ShuttleUid!.Value), out var record), Is.True);
                Assert.That(record!.PurchasePrice, Is.EqualTo(expectedPrice));
            }
            finally
            {
                maps.DeleteMap(mapId);
                entities.DeleteEntity(station);
            }
        });

        await pair.CleanReturnAsync();
    }
}
