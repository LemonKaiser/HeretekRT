using Content.Server._ES.TileFires;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class TileFireCompatibilityTest
{
    [Test]
    public async Task TileFireRespectsKoronusSafeZones()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var inside = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
        var outside = new EntityCoordinates(map.Grid, 4.5f, 0.5f);
        EntityUid directFire = default;

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, outside, map.Tile.Tile);
            var zone = entities.SpawnEntity(null, map.GridCoords);
            var safety = entities.AddComponent<KoronusSafetyZoneComponent>(zone);
            safety.Profile = "KoronusStationSafeZone";
            safety.Radius = 2f;
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var safety = server.System<KoronusSafetyPolicySystem>();
            var transform = server.System<SharedTransformSystem>();
            var insideMap = transform.ToMapCoordinates(inside);
            var outsideMap = transform.ToMapCoordinates(outside);
            Assert.That(safety.HasRule(insideMap.MapId, insideMap.Position, KoronusSafetyRule.FireAndHighTemperature), Is.True);
            Assert.That(safety.HasRule(outsideMap.MapId, outsideMap.Position, KoronusSafetyRule.FireAndHighTemperature), Is.False);

            var fire = server.System<ESTileFireSystem>();
            Assert.That(fire.TryDoTileFire(inside), Is.False);
            Assert.That(fire.TryDoTileFire(outside), Is.True);

            directFire = entities.SpawnEntity("ESTileFire", inside);
            Assert.That(entities.GetComponent<FlammableComponent>(directFire).OnFire, Is.False);
        });

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(entities.EntityExists(directFire), Is.False));
        await pair.CleanReturnAsync();
    }
}
