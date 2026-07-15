using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Mono.Planets;
using Content.Server.Atmos.Components;
using Content.Server.Power.Components;
using Content.Server._WH40K.SectorMap.Components;
using Content.Server._WH40K.SectorMap.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Shared.Parallax;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Tests;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests.Shuttle;

/// <summary>
/// Covers the server-authoritative planetary transfer without involving normal FTL or docking.
/// The test creates the same runtime map registry used by the Koronus bootstrap, then exercises
/// the public landing system directly so invalid messages cannot provide map coordinates.
/// </summary>
[NonParallelizable]
public sealed class PlanetaryLandingIntegrationTest : ContentUnitTest
{
    private const string SystemId = "PlanetaryLandingTestSystem";
    private const string SurfaceId = "PlanetaryLandingTestSurface";
    private const string BodyId = "PlanetaryLandingTestBody";
    private const string SiteId = "PlanetaryLandingTestMainPad";
    private const string ReservationKey = BodyId + "/" + SiteId;
    private const string FoulstoneSystemId = "Trinnitos";
    private const string FoulstoneSurfaceId = "FoulstoneSurface";
    private const string FoulstoneBodyId = "Foulstone";
    private const string FoulstoneSiteId = "FoulstoneMainPad";
    private const string BoundarySystemId = "KoronusBoundaryTestSystem";

    [TestPrototypes]
    private const string TestPrototypes = """
        - type: koronusSystem
          id: PlanetaryLandingTestSystem
          sector: KoronusExpanse
          displayName: Planetary landing integration test
          spaceMode: Planetary
          enabled: true

        - type: koronusCelestialBody
          id: PlanetaryLandingTestBody
          system: PlanetaryLandingTestSystem
          bodyType: Planet
          displayName: Planetary landing integration test body
          orbitRadius: 2400
          orbitPhase: 215
          surface: PlanetaryLandingTestSurface

        - type: koronusPlanetSurface
          id: PlanetaryLandingTestSurface
          mapPath: /Maps/_WH40K/Planets/foulstone_surface.yml
          preloadOnRoundStart: false
          playableSize: 100, 100
          sceneryBuffer: 0
          orbitalLaunchDistance: 500

        - type: koronusSystem
          id: KoronusBoundaryTestSystem
          sector: KoronusExpanse
          displayName: Koronus boundary integration test system
          enabled: true
          boundaryRadius: 50
        """;

    [Test]
    public async Task TrinnitosInitialGridSpawnUsesConfiguredSolarRadius()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var system = prototypes.Index<KoronusSystemPrototype>(FoulstoneSystemId);
                Assert.That(system.InitialGridSpawnDistance, Is.EqualTo(3000f));

                foreach (var angle in new[] { Angle.Zero, Angle.FromDegrees(90f), Angle.FromDegrees(217f) })
                {
                    var position = KoronusSectorRuleSystem.GetInitialGridSpawnPosition(system, angle);
                    Assert.That(Vector2.Distance(position, system.StellarCenter), Is.EqualTo(3000f).Within(0.01f));
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task SectorArrivalIsMeasuredFromTheAuthoredNavigationCenter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var system = prototypes.Index<KoronusSystemPrototype>(FoulstoneSystemId);
                var localBounds = new Box2(-6f, -4f, 10f, 8f);
                var physicsCenter = new Vector2(-1f, 0.5f);
                var requestedPhysicsCenter = ShuttleConsoleSystem.GetKoronusArrivalPosition(
                    system,
                    localBounds,
                    physicsCenter,
                    Angle.FromDegrees(90f));

                // ConsoleFTL performs this conversion immediately before the actual FTL movement.
                var transformOrigin = requestedPhysicsCenter - physicsCenter;
                var resultingGridCenter = transformOrigin + localBounds.Center;
                Assert.That(
                    Vector2.Distance(resultingGridCenter, system.NavigationCenter),
                    Is.EqualTo(system.ArrivalDistance).Within(0.01f));

                foreach (var corner in new[]
                         {
                             localBounds.BottomLeft,
                             localBounds.BottomRight,
                             localBounds.TopLeft,
                             localBounds.TopRight,
                         })
                {
                    Assert.That(
                        Vector2.Distance(transformOrigin + corner, system.NavigationCenter),
                        Is.LessThan(system.BoundaryRadius));
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task SectorBoundaryStopsShuttleWithoutAPhysicalWall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var sector = entMan.System<KoronusSectorRuleSystem>();
        var physicsSystem = entMan.System<SharedPhysicsSystem>();
        var shuttle = EntityUid.Invalid;

        try
        {
            await server.WaitAssertion(() =>
            {
                sector.ConfigureSystemMap(
                    orbital.MapUid,
                    prototypes.Index<KoronusSystemPrototype>(BoundarySystemId));
                shuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(100f, 0f), 4, 4);
                var physics = entMan.GetComponent<PhysicsComponent>(shuttle);
                physicsSystem.SetLinearVelocity(shuttle, new Vector2(20f, 5f), body: physics);
                physicsSystem.SetAngularVelocity(shuttle, 1f, body: physics);
            });

            // Allow the low-frequency local root index to catch entities spawned directly on an
            // already registered terrain grid. Parent changes remain event-driven.
            await server.WaitRunTicks(70);
            await server.WaitAssertion(() =>
            {
                var bounds = physicsSystem.GetWorldAABB(shuttle);
                foreach (var corner in new[] { bounds.BottomLeft, bounds.BottomRight, bounds.TopLeft, bounds.TopRight })
                    Assert.That(corner.Length(), Is.LessThanOrEqualTo(50f));

                var physics = entMan.GetComponent<PhysicsComponent>(shuttle);
                Assert.That(physics.LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(physics.AngularVelocity, Is.Zero);

                var physicalBoundaryCount = 0;
                var children = entMan.GetComponent<TransformComponent>(orbital.MapUid).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (entMan.HasComponent<BoundaryComponent>(child))
                        physicalBoundaryCount++;
                }

                Assert.That(physicalBoundaryCount, Is.Zero,
                    "Космическая граница не должна создавать невидимую физическую стену.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task LandingAndLaunchKeepTheSameGridAndReleaseTheReservation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var physicsSystem = entMan.System<SharedPhysicsSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var ruleEntity = ConfigureTestSystem(entMan, orbital, surface);
                try
                {
                var shuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(120f, 0f), 4, 4);
                var physics = entMan.GetComponent<PhysicsComponent>(shuttle);
                physicsSystem.SetLinearVelocity(shuttle, new Vector2(7f, -3f), body: physics);
                physicsSystem.SetAngularVelocity(shuttle, 1f, body: physics);

                Assert.That(planetary.TryLand(shuttle, BodyId, SiteId, out var landingFailure), Is.True,
                    $"Посадка должна пройти: {landingFailure}");
                Assert.That(xform.GetMapCoordinates(shuttle).MapId, Is.EqualTo(surface.MapId));
                var landedState = planetary.GetInterfaceState(shuttle);
                Assert.That(landedState.NavigationSuppressed, Is.True,
                    "На поверхности планеты NAV и БСС должны сообщать об отсутствии сигнала.");
                Assert.That(landedState.Bodies.Single(body => body.Id == BodyId).LandingSites.Single(site => site.Id == SiteId).ReservedByCurrentShuttle,
                    Is.True);
                Assert.That(physics.LinearVelocity, Is.EqualTo(Vector2.Zero));
                Assert.That(physics.AngularVelocity, Is.Zero);

                var landingPosition = xform.GetWorldPosition(entMan.GetComponent<TransformComponent>(shuttle));
                xform.SetWorldPosition(shuttle, landingPosition + Vector2.UnitX * 50f);
                Assert.That(planetary.TryLaunch(shuttle, out var offPadFailure), Is.False);
                Assert.That(offPadFailure, Is.EqualTo(KoronusPlanetaryTransferFailure.NotOnLandingSite));
                xform.SetWorldPosition(shuttle, landingPosition);

                Assert.That(planetary.TryLaunch(shuttle, out var launchFailure), Is.True,
                    $"Взлёт владельца площадки должен пройти: {launchFailure}");
                Assert.That(xform.GetMapCoordinates(shuttle).MapId, Is.EqualTo(orbital.MapId));
                var orbitalState = planetary.GetInterfaceState(shuttle);
                Assert.That(orbitalState.CanLaunch, Is.False);
                Assert.That(orbitalState.NavigationSuppressed, Is.False,
                    "После возвращения на орбиту навигационный сигнал должен восстановиться.");
                var prototypes = server.ResolveDependency<IPrototypeManager>();
                var timing = server.ResolveDependency<IGameTiming>();
                var body = prototypes.Index<KoronusCelestialBodyPrototype>(BodyId);
                var system = prototypes.Index<KoronusSystemPrototype>(SystemId);
                var phase = (body.OrbitPhase + body.OrbitAngularSpeed * (float) timing.CurTime.TotalSeconds) *
                            MathF.PI / 180f;
                var bodyPosition = system.StellarCenter +
                                   new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * body.OrbitRadius;
                var orbitalPosition = xform.GetMapCoordinates(shuttle).Position +
                                      entMan.GetComponent<MapGridComponent>(shuttle).LocalAABB.Center;
                Assert.That(Vector2.Distance(orbitalPosition, bodyPosition), Is.EqualTo(500f).Within(0.5f),
                    "После взлёта шаттл должен находиться в заданном радиусе исходной планеты.");
                Assert.That(Vector2.Dot(orbitalPosition - bodyPosition, bodyPosition - system.StellarCenter),
                    Is.GreaterThan(0f), "Точка выхода должна лежать с внешней стороны орбиты планеты.");
                }
                finally
                {
                    entMan.DeleteEntity(ruleEntity);
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task LandingRejectsOccupiedSiteAndOversizedShuttleWithoutMovingEither()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var ruleEntity = ConfigureTestSystem(entMan, orbital, surface);
                try
                {
                var first = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(100f, 0f), 4, 4);
                var second = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(140f, 0f), 4, 4);
                var oversized = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(180f, 0f), 24, 4);

                Assert.That(planetary.TryLand(first, BodyId, SiteId, out var firstFailure), Is.True,
                    $"Первый шаттл должен занять площадку: {firstFailure}");
                Assert.That(planetary.TryLand(second, BodyId, SiteId, out var occupiedFailure), Is.False);
                Assert.That(occupiedFailure, Is.EqualTo(KoronusPlanetaryTransferFailure.LandingSiteOccupied));
                Assert.That(xform.GetMapCoordinates(second).MapId, Is.EqualTo(orbital.MapId));
                Assert.That(planetary.GetInterfaceState(first).CanLaunch, Is.True);

                Assert.That(planetary.TryLaunch(first, out var launchFailure), Is.True,
                    $"Первый шаттл должен освободить площадку: {launchFailure}");
                Assert.That(planetary.TryLand(oversized, BodyId, SiteId, out var sizeFailure), Is.False);
                Assert.That(sizeFailure, Is.EqualTo(KoronusPlanetaryTransferFailure.ShuttleTooLarge));
                Assert.That(xform.GetMapCoordinates(oversized).MapId, Is.EqualTo(orbital.MapId));
                Assert.That(planetary.GetInterfaceState(oversized).Bodies.Single(body => body.Id == BodyId).LandingSites.Single(site => site.Id == SiteId).Occupied,
                    Is.False);
                }
                finally
                {
                    entMan.DeleteEntity(ruleEntity);
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task LandingRejectsFtlAndDockedShuttles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var ruleEntity = ConfigureTestSystem(entMan, orbital, surface);
                try
                {
                var ftlShuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(100f, 0f), 4, 4);
                // Cooldown is a real non-available FTL state that does not require the full in-flight
                // hyperspace setup used by the travelling state.
                entMan.EnsureComponent<FTLComponent>(ftlShuttle).State = FTLState.Cooldown;
                Assert.That(planetary.TryLand(ftlShuttle, BodyId, SiteId, out var ftlFailure), Is.False);
                Assert.That(ftlFailure, Is.EqualTo(KoronusPlanetaryTransferFailure.ShuttleInFtl));

                var dockedShuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(140f, 0f), 4, 4);
                var dock = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(dockedShuttle, 0.5f, 0.5f));
                var otherDock = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(dockedShuttle, 1.5f, 0.5f));
                var dockComponent = entMan.GetComponent<DockingComponent>(dock);
                // This validates the landing gate in isolation. Clear the intentionally minimal marker
                // before another game tick, because it is not a complete physical docking configuration.
                dockComponent.DockedWith = otherDock;
                try
                {
                    Assert.That(planetary.TryLand(dockedShuttle, BodyId, SiteId, out var dockingFailure), Is.False);
                    Assert.That(dockingFailure, Is.EqualTo(KoronusPlanetaryTransferFailure.ShuttleDocked));
                }
                finally
                {
                    dockComponent.DockedWith = null;
                }
                }
                finally
                {
                    entMan.DeleteEntity(ruleEntity);
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task DestroyedLandedShuttleReleasesItsReservation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var ruleEntity = EntityUid.Invalid;
        var second = EntityUid.Invalid;

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = ConfigureTestSystem(entMan, orbital, surface);
                var first = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(100f, 0f), 4, 4);
                second = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(140f, 0f), 4, 4);
                Assert.That(planetary.TryLand(first, BodyId, SiteId, out var failure), Is.True,
                    $"Первый шаттл должен занять площадку: {failure}");
                entMan.DeleteEntity(first);
            });

            await server.WaitRunTicks(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(planetary.TryLand(second, BodyId, SiteId, out var failure), Is.True,
                    $"Удалённый шаттл не должен удерживать reservation: {failure}");
                entMan.DeleteEntity(ruleEntity);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task SurfaceBoundaryClampsLandedShuttleToThePlayableSquare()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var ruleEntity = EntityUid.Invalid;
        var shuttle = EntityUid.Invalid;
        var walker = EntityUid.Invalid;
        var carried = EntityUid.Invalid;

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = ConfigureTestSystem(entMan, orbital, surface);
                shuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(120f, 0f), 4, 4);
                Assert.That(planetary.TryLand(shuttle, BodyId, SiteId, out var failure), Is.True,
                    $"Шаттл должен приземлиться до проверки границы: {failure}");
                xform.SetWorldPosition(shuttle, new Vector2(500f, 500f));

                walker = entMan.SpawnEntity("MobHuman", surface.GridCoords);
                // Keep the standalone mover away from the out-of-bounds shuttle so grid traversal
                // cannot correctly parent it to the shuttle as a passenger.
                xform.SetWorldPosition(walker, new Vector2(500f, -500f));
                carried = entMan.SpawnEntity(null, surface.GridCoords);
                xform.SetParent(carried, walker);
            });

            await server.WaitRunTicks(10);
            await server.WaitAssertion(() =>
            {
                var bounds = physics.GetWorldAABB(shuttle);
                Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(-50f));
                Assert.That(bounds.Right, Is.LessThanOrEqualTo(50f));
                Assert.That(bounds.Bottom, Is.GreaterThanOrEqualTo(-50f));
                Assert.That(bounds.Top, Is.LessThanOrEqualTo(50f));

                var walkerPosition = xform.GetWorldPosition(walker);
                Assert.That(walkerPosition.X, Is.InRange(-50f, 50f));
                Assert.That(walkerPosition.Y, Is.InRange(-50f, 50f));

                Assert.That(entMan.HasComponent<KoronusSurfaceBoundaryTrackedComponent>(walker), Is.True,
                    "Корневая движущаяся сущность поверхности должна проверяться по событию движения.");
                Assert.That(entMan.HasComponent<KoronusSurfaceBoundaryTrackedComponent>(carried), Is.False,
                    "Содержимое персонажа не должно попадать в отдельный цикл контроля границы.");

                var physicalBoundaryCount = 0;
                var children = entMan.GetComponent<TransformComponent>(surface.MapUid).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (entMan.HasComponent<BoundaryComponent>(child))
                        physicalBoundaryCount++;
                }

                Assert.That(physicalBoundaryCount, Is.Zero,
                    "Планетарная граница не должна ударять шаттлы физической стеной.");
                var visualBoundary = entMan.GetComponent<KoronusPlanetSurfaceBoundaryComponent>(surface.MapUid);
                Assert.That(visualBoundary.Minimum, Is.EqualTo(new Vector2(-50f, -50f)));
                Assert.That(visualBoundary.Maximum, Is.EqualTo(new Vector2(50f, 50f)));
                entMan.DeleteEntity(ruleEntity);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task EmptySurfaceRepausesThroughSectorResidencyPolicy()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var serverMaps = entMan.System<MapSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var ruleEntity = EntityUid.Invalid;

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = ConfigureTestSystem(entMan, orbital, surface);
                var shuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(160f, 0f), 4, 4);
                Assert.That(planetary.TryLand(shuttle, BodyId, SiteId, out var failure), Is.True,
                    $"Шаттл должен разбудить поверхность до проверки residency: {failure}");
                Assert.That(serverMaps.IsPaused(surface.MapId), Is.False);

            });

            // The prototype uses the standard ten-second grace interval. Leave a margin for the
            // residency system's 250 ms update cadence rather than mutating its private timer.
            await server.WaitRunTicks(330);
            await server.WaitAssertion(() =>
            {
                Assert.That(serverMaps.IsPaused(surface.MapId), Is.True);
                entMan.DeleteEntity(ruleEntity);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task FoulstoneLandingAndLaunchUseAtmosphericTransitSpace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var ruleEntity = EntityUid.Invalid;
        var shuttle = EntityUid.Invalid;

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = ConfigureFoulstone(entMan, orbital, surface);
                shuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, Vector2.Zero, 4, 4);

                Assert.That(planetary.TryLand(shuttle, FoulstoneBodyId, FoulstoneSiteId, out var farFailure), Is.False);
                Assert.That(farFailure, Is.EqualTo(KoronusPlanetaryTransferFailure.OutsideLandingApproach));

                var foulstonePosition = GetCurrentCelestialPosition(prototypes, timing, FoulstoneSystemId, FoulstoneBodyId);
                xform.SetWorldPosition(shuttle, foulstonePosition);

                Assert.That(planetary.GetInterfaceState(shuttle).Bodies.Single(body => body.Id == FoulstoneBodyId).InLandingApproachRange,
                    Is.True);
                Assert.That(planetary.TryLand(shuttle, FoulstoneBodyId, FoulstoneSiteId, out var landingFailure), Is.True,
                    $"Посадка на Foulstone должна начаться: {landingFailure}");
                Assert.That(entMan.HasComponent<KoronusPlanetaryTransitComponent>(shuttle), Is.True);
                Assert.That(xform.GetMapCoordinates(shuttle).MapId, Is.EqualTo(orbital.MapId));
            });

            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<KoronusPlanetaryTransitComponent>(shuttle), Is.True);
                Assert.That(xform.GetMapCoordinates(shuttle).MapId, Is.EqualTo(orbital.MapId));
            });

            // The first five seconds are the BSS-like spool-up. The flight and arrival phases move the shuttle
            // through a dedicated atmospheric map, never through the ordinary FTL map or sector NAV map.
            await server.WaitRunTicks(330);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<KoronusPlanetaryTransitComponent>(shuttle), Is.True,
                    "Компонент atmospheric transit не должен исчезнуть до завершения трёх фаз.");
                var transit = entMan.GetComponent<KoronusPlanetaryTransitComponent>(shuttle);
                Assert.That(transit.EnteredTransitSpace, Is.True,
                    "После стартовой фазы шаттл должен войти в атмосферное transit-пространство.");
                var transitMapId = xform.GetMapCoordinates(shuttle).MapId;
                Assert.That(transitMapId, Is.Not.EqualTo(orbital.MapId));
                Assert.That(transitMapId, Is.Not.EqualTo(surface.MapId));
                var transitMapUid = mapSystem.GetMapOrInvalid(transitMapId);
                Assert.That(entMan.GetComponent<ParallaxComponent>(transitMapUid).Parallax, Is.EqualTo("AtmosphericTransit"));
                Assert.That(planetary.GetInterfaceState(shuttle).NavigationSuppressed, Is.True);
            });

            // Foulstone uses 5 s startup + 10 s flight + 5 s arrival. The test server runs at 30 ticks/s.
            await server.WaitRunTicks(900);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<KoronusPlanetaryTransitComponent>(shuttle), Is.False);
                Assert.That(xform.GetMapCoordinates(shuttle).MapId, Is.EqualTo(surface.MapId));
                var rule = entMan.GetComponent<KoronusSectorRuleComponent>(ruleEntity);
                Assert.That(rule.LandingReservations.Count, Is.EqualTo(1),
                    $"Резервация исчезла после атмосферной посадки; сессий: {rule.LandingSessions.Count}.");
                Assert.That(planetary.TryLaunch(shuttle, out var launchFailure), Is.True,
                    $"Взлёт с Foulstone должен начаться: {launchFailure}");
                Assert.That(entMan.HasComponent<KoronusPlanetaryTransitComponent>(shuttle), Is.True);
                Assert.That(xform.GetMapCoordinates(shuttle).MapId, Is.EqualTo(surface.MapId));
            });

            await server.WaitRunTicks(330);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<KoronusPlanetaryTransitComponent>(shuttle), Is.True,
                    "Компонент atmospheric transit не должен исчезнуть до завершения трёх фаз.");
                var transit = entMan.GetComponent<KoronusPlanetaryTransitComponent>(shuttle);
                Assert.That(transit.EnteredTransitSpace, Is.True,
                    "После стартовой фазы шаттл должен войти в атмосферное transit-пространство.");
                var transitMapId = xform.GetMapCoordinates(shuttle).MapId;
                Assert.That(transitMapId, Is.Not.EqualTo(orbital.MapId));
                Assert.That(transitMapId, Is.Not.EqualTo(surface.MapId));
                var transitMapUid = mapSystem.GetMapOrInvalid(transitMapId);
                Assert.That(entMan.GetComponent<ParallaxComponent>(transitMapUid).Parallax, Is.EqualTo("AtmosphericTransit"));
                Assert.That(planetary.GetInterfaceState(shuttle).NavigationSuppressed, Is.True);
            });

            await server.WaitRunTicks(900);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<KoronusPlanetaryTransitComponent>(shuttle), Is.False);
                Assert.That(xform.GetMapCoordinates(shuttle).MapId, Is.EqualTo(orbital.MapId));
                entMan.DeleteEntity(ruleEntity);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task MobLeavingShuttleDuringAtmosphericTransitFallsToPlanetAndTakesDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var ruleEntity = EntityUid.Invalid;
        var shuttle = EntityUid.Invalid;
        var passenger = EntityUid.Invalid;
        var bluntBeforeFall = FixedPoint2.Zero;

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = ConfigureFoulstone(entMan, orbital, surface);

                // Guarantee at least one materialised, unobstructed tile outside the landing pad.
                var terrain = entMan.GetComponent<MapGridComponent>(surface.Grid);
                mapSystem.SetTile(surface.Grid, terrain, new Vector2i(30, 0), new Tile(1));

                var foulstonePosition = GetCurrentCelestialPosition(prototypes, timing, FoulstoneSystemId, FoulstoneBodyId);
                shuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, foulstonePosition, 4, 4);
                passenger = entMan.SpawnEntity("MobHuman", new EntityCoordinates(shuttle, new Vector2(1.5f, 1.5f)));

                Assert.That(planetary.TryLand(shuttle, FoulstoneBodyId, FoulstoneSiteId, out var failure), Is.True,
                    $"Посадка на Foulstone должна начаться: {failure}");
            });

            await server.WaitRunTicks(330);
            await server.WaitAssertion(() =>
            {
                var transit = entMan.GetComponent<KoronusPlanetaryTransitComponent>(shuttle);
                Assert.That(transit.EnteredTransitSpace, Is.True);
                Assert.That(transit.TransitMobs, Does.Contain(passenger),
                    "Моб на борту должен отслеживаться до конца атмосферного перелёта.");
                entMan.GetComponent<DamageableComponent>(passenger).Damage.DamageDict
                    .TryGetValue("Blunt", out bluntBeforeFall);

                var shuttleCoordinates = xform.GetMapCoordinates(shuttle);
                var transitMapUid = mapSystem.GetMapOrInvalid(shuttleCoordinates.MapId);
                xform.SetCoordinates(passenger,
                    new EntityCoordinates(transitMapUid, shuttleCoordinates.Position + new Vector2(10f, 0f)));
                Assert.That(entMan.GetComponent<TransformComponent>(passenger).GridUid, Is.Not.EqualTo(shuttle));
            });

            await server.WaitRunTicks(10);
            await server.WaitAssertion(() =>
            {
                var passengerCoordinates = xform.GetMapCoordinates(passenger);
                Assert.That(passengerCoordinates.MapId, Is.EqualTo(surface.MapId),
                    "Выпавший при спуске моб должен оказаться на поверхности планеты.");
                var passengerGrid = entMan.GetComponent<TransformComponent>(passenger).GridUid;
                Assert.That(passengerGrid, Is.Not.Null);
                Assert.That(passengerGrid!.Value, Is.EqualTo(surface.Grid.Owner));
                Assert.That(entMan.GetComponent<DamageableComponent>(passenger).Damage.DamageDict
                        .TryGetValue("Blunt", out var bluntAfterFall), Is.True);
                Assert.That(bluntAfterFall - bluntBeforeFall, Is.EqualTo(FixedPoint2.New(200)));

                // The Foulstone pad is 9 x 5 m and the fall selector reserves another 2 m around it.
                var position = passengerCoordinates.Position;
                var padCenter = new Vector2(-0.5f, -0.5f);
                Assert.That(MathF.Abs(position.X - padCenter.X) > 6.5f ||
                            MathF.Abs(position.Y - padCenter.Y) > 4.5f, Is.True,
                    "Точка падения не должна находиться на посадочной площадке.");
                entMan.DeleteEntity(ruleEntity);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task FoulstonePreloadBuildsAnAtmosphericBiomeSurface()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var landingPads = entMan.System<KoronusLandingPadSystem>();
        var ruleEntity = EntityUid.Invalid;

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = entMan.SpawnEntity("KoronusSectorBootstrap", new MapCoordinates(Vector2.Zero, MapId.Nullspace));
                var rule = entMan.GetComponent<KoronusSectorRuleComponent>(ruleEntity);
                planetary.PreloadSurfaces((ruleEntity, rule));

                Assert.That(planetary.TryGetSurfaceMap(FoulstoneSurfaceId, out var surfaceMap), Is.True);
                var surfaceMapUid = mapSystem.GetMapOrInvalid(surfaceMap);
                Assert.That(surfaceMapUid, Is.Not.EqualTo(EntityUid.Invalid));
                Assert.That(entMan.HasComponent<PlanetMapComponent>(surfaceMapUid), Is.True);
                Assert.That(entMan.HasComponent<BiomeComponent>(surfaceMapUid), Is.True);
                Assert.That(entMan.HasComponent<GravityComponent>(surfaceMapUid), Is.True);
                Assert.That(entMan.HasComponent<MapAtmosphereComponent>(surfaceMapUid), Is.True);

                var parallax = entMan.GetComponent<ParallaxComponent>(surfaceMapUid);
                Assert.That(parallax.Parallax, Is.EqualTo("Blank"));

                var runtime = entMan.GetComponent<KoronusPlanetSurfaceMapComponent>(surfaceMapUid);
                Assert.That(runtime.PlayableBounds, Is.EqualTo(new Box2(-50f, -50f, 50f, 50f)));
                Assert.That(runtime.GenerationBounds, Is.EqualTo(new Box2(-75f, -75f, 75f, 75f)));
                Assert.That(runtime.TerrainGrid, Is.EqualTo(surfaceMapUid));
                var visualBoundary = entMan.GetComponent<KoronusPlanetSurfaceBoundaryComponent>(surfaceMapUid);
                Assert.That(visualBoundary.Minimum, Is.EqualTo(new Vector2(-50f, -50f)));
                Assert.That(visualBoundary.Maximum, Is.EqualTo(new Vector2(50f, 50f)));

                var generationBounds = entMan.GetComponent<BiomeGenerationBoundsComponent>(surfaceMapUid);
                Assert.That(generationBounds.Bounds, Is.EqualTo(runtime.GenerationBounds));

                // Planet grids intentionally do not maintain collision AABBs. Verify the actual
                // materialised turf and the biome's permanent no-spawn reservation instead.
                var terrain = entMan.GetComponent<MapGridComponent>(surfaceMapUid);
                foreach (var index in new[]
                         {
                             new Vector2i(-25, -25), new Vector2i(-25, 24),
                             new Vector2i(24, -25), new Vector2i(24, 24),
                         })
                {
                    Assert.That(mapSystem.TryGetTileRef(surfaceMapUid, terrain, index, out var tile), Is.True);
                    Assert.That(tile.Tile.IsEmpty, Is.False);
                }

                var biome = entMan.GetComponent<BiomeComponent>(surfaceMapUid);
                Assert.That(biome.ModifiedTiles.Values.Sum(tiles => tiles.Count), Is.GreaterThanOrEqualTo(2500));

                var pads = landingPads.GetPads(runtime.TerrainGrid);
                Assert.That(pads, Has.Count.EqualTo(1));
                Assert.That(pads[0].Id, Is.EqualTo(FoulstoneSiteId));
                Assert.That(pads[0].Size, Is.EqualTo(new Vector2(9f, 5f)));
                Assert.That(pads[0].Component.Enabled, Is.True);
                Assert.That(pads[0].Component.PublicAccess, Is.True);

                Assert.That(entMan.TryGetComponent(pads[0].Console, out KoronusLandingPadConsoleComponent console), Is.True);
                Assert.That(console!.Locked, Is.True);
                Assert.That(console.ParkingTime, Is.EqualTo(600));
                Assert.That(entMan.TryGetComponent(pads[0].Console, out ApcPowerReceiverComponent power), Is.True);
                Assert.That(power!.NeedsPower, Is.False,
                    "Официальная консоль должна работать без внешней энергосети, не меняя прототип для игроков.");
                Assert.That(landingPads.TryConfigure(
                    pads[0].Console,
                    "Взломано",
                    0,
                    false,
                    false), Is.False,
                    "Заблокированная официальная площадка не должна принимать изменения настроек.");
                entMan.DeleteEntity(ruleEntity);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task FoulstoneBiomeStopsAtTheTwentyFiveTileVisualBuffer()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var serverMaps = entMan.System<MapSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var ruleEntity = EntityUid.Invalid;
        var player = EntityUid.Invalid;

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = entMan.SpawnEntity("KoronusSectorBootstrap", new MapCoordinates(Vector2.Zero, MapId.Nullspace));
                var rule = entMan.GetComponent<KoronusSectorRuleComponent>(ruleEntity);
                planetary.PreloadSurfaces((ruleEntity, rule));

                Assert.That(planetary.TryGetSurfaceMap(FoulstoneSurfaceId, out var surfaceMap), Is.True);
                var surfaceMapUid = mapSystem.GetMapOrInvalid(surfaceMap);
                serverMaps.SetPaused(surfaceMap, false);

                // The player can stand at the physical edge (50 m). Biome PVS loading then needs
                // the visual tiles up to x=74, but may not create x=75 or anything beyond it.
                player = entMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(49f, 0f), surfaceMap));
                server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), player);
            });

            await server.WaitRunTicks(6);
            await server.WaitAssertion(() =>
            {
                Assert.That(planetary.TryGetSurfaceMap(FoulstoneSurfaceId, out var surfaceMap), Is.True);
                var surfaceMapUid = mapSystem.GetMapOrInvalid(surfaceMap);
                var terrain = entMan.GetComponent<MapGridComponent>(surfaceMapUid);

                Assert.That(mapSystem.TryGetTileRef(surfaceMapUid, terrain, new Vector2i(74, 0), out var bufferTile), Is.True);
                Assert.That(bufferTile.Tile.IsEmpty, Is.False);

                var hasOutsideTile = mapSystem.TryGetTileRef(surfaceMapUid, terrain, new Vector2i(75, 0), out var outsideTile);
                Assert.That(hasOutsideTile && !outsideTile.Tile.IsEmpty, Is.False);

                var biome = entMan.GetComponent<BiomeComponent>(surfaceMapUid);
                Assert.That(biome.LoadedEntities.Values.SelectMany(chunk => chunk.Values)
                        .All(index => index.X >= -75 && index.X < 75 && index.Y >= -75 && index.Y < 75),
                    Is.True);
                Assert.That(biome.LoadedDecals.Values.SelectMany(chunk => chunk.Values)
                        .All(index => index.X >= -75 && index.X < 75 && index.Y >= -75 && index.Y < 75),
                    Is.True);

                server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), null);
            });

            // Allow the detach state to reach the connected client. The pooled pair owns final
            // entity cleanup; deleting a streamed procedural map while the client is connected
            // races PVS serialization of its generated biome entities.
            await server.WaitRunTicks(2);
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task EntityLandingPadsUseCardinalGroupsAndOnePrimaryConsole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var pads = entMan.System<KoronusLandingPadSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var map = entMan.System<SharedMapSystem>();
                var grid = entMan.GetComponent<MapGridComponent>(surface.Grid.Owner);
                var foundation = new List<(Vector2i, Tile)>();
                for (var x = 0; x < 8; x++)
                {
                    for (var y = 0; y < 3; y++)
                        foundation.Add((new Vector2i(x, y), new Tile(1)));
                }
                map.SetTiles(surface.Grid.Owner, grid, foundation);

                var firstConsole = CreateLandingPad(entMan, surface.Grid.Owner, new Vector2i(0, 0), 3, 2, "first", 0);
                var conflictingConsole = entMan.SpawnEntity(
                    "KoronusLandingPadConsole",
                    new EntityCoordinates(surface.Grid.Owner, new Vector2(1.5f, 2.5f)));
                SetAlwaysPowered(entMan, conflictingConsole);
                var secondConsole = CreateLandingPad(entMan, surface.Grid.Owner, new Vector2i(5, 0), 2, 2, "second", 0);

                var resolved = pads.GetPads(surface.Grid.Owner);
                Assert.That(resolved, Has.Count.EqualTo(2),
                    "Зазор между группами обязан образовывать две отдельные площадки.");
                var first = resolved.Single(pad => pad.Id == "first");
                Assert.That(first.Tiles, Has.Count.EqualTo(6));
                Assert.That(first.Console, Is.EqualTo(firstConsole));
                Assert.That(first.Consoles, Does.Contain(conflictingConsole));
                Assert.That(first.Consoles, Has.Length.EqualTo(2));
                Assert.That(resolved.Single(pad => pad.Id == "second").Console, Is.EqualTo(secondConsole));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task ParkingDeadlineLaunchesShuttleAndStartsLandingCooldown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var shuttle = EntityUid.Invalid;
        var ruleEntity = EntityUid.Invalid;

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = ConfigureTestSystem(entMan, orbital, surface, createDefaultPad: false);
                var siteId = "timed-pad";
                CreateLandingPad(entMan, surface.Grid.Owner, new Vector2i(-5, -3), 9, 5, siteId, 1);
                shuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(120f, 0f), 4, 4);

                Assert.That(planetary.TryLand(shuttle, BodyId, siteId, out var failure), Is.True,
                    $"Посадка на сущностную площадку должна пройти: {failure}");
                var rule = entMan.GetComponent<KoronusSectorRuleComponent>(ruleEntity);
                Assert.That(rule.LandingSessions, Has.Count.EqualTo(1));
                Assert.That(rule.LandingSessions.Values.Single().Deadline, Is.Not.Null);
            });

            await server.WaitRunTicks(40);
            await server.WaitAssertion(() =>
            {
                Assert.That(xform.GetMapCoordinates(shuttle).MapId, Is.EqualTo(orbital.MapId),
                    "По окончании стоянки исправный шаттл должен выполнить обычный взлёт.");
                Assert.That(entMan.HasComponent<KoronusPlanetaryLandingCooldownComponent>(shuttle), Is.True);
                Assert.That(planetary.TryLand(shuttle, BodyId, "timed-pad", out var cooldownFailure), Is.False);
                Assert.That(cooldownFailure, Is.EqualTo(KoronusPlanetaryTransferFailure.LandingCooldown));

                var cooldown = entMan.GetComponent<KoronusPlanetaryLandingCooldownComponent>(shuttle);
                Assert.That((cooldown.Until - server.Timing.CurTime).TotalSeconds, Is.InRange(59d, 60d));
                entMan.DeleteEntity(ruleEntity);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task PersonnelTeleporterTransfersOneMobAfterFiveSecondSpinup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var serverMaps = entMan.System<MapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var ruleEntity = EntityUid.Invalid;
        var passenger = EntityUid.Invalid;

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = ConfigureTestSystem(entMan, orbital, surface);
                var shuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(120f, 0f), 4, 4);
                var source = entMan.SpawnEntity(
                    "KoronusPlanetaryTeleporter",
                    new EntityCoordinates(shuttle, new Vector2(1.5f, 1.5f)));
                // Reuse the connected landing-pad foundation. An isolated turf would be split into
                // another grid by normal grid-fixture processing and would no longer represent the
                // registered planetary terrain grid exercised by this test.
                var target = entMan.SpawnEntity(
                    "KoronusPlanetaryTeleporter",
                    new EntityCoordinates(surface.Grid.Owner, new Vector2(0.5f, 0.5f)));
                SetAlwaysPowered(entMan, source);
                SetAlwaysPowered(entMan, target);
                serverMaps.SetPaused(surface.MapId, true);
                Assert.That(entMan.System<KoronusPlanetaryTeleporterSystem>()
                    .SetSelectedTarget(source, $"teleporter:{target}"), Is.True);
                passenger = entMan.SpawnEntity(
                    "MobHuman",
                    new EntityCoordinates(shuttle, new Vector2(1.5f, 1.5f)));
            });

            await server.WaitRunTicks(140);
            await server.WaitAssertion(() =>
                Assert.That(xform.GetMapCoordinates(passenger).MapId, Is.EqualTo(orbital.MapId),
                    "До истечения пяти секунд пассажир не должен перемещаться."));

            await server.WaitRunTicks(20);
            await server.WaitAssertion(() =>
            {
                Assert.That(xform.GetMapCoordinates(passenger).MapId, Is.EqualTo(surface.MapId));
                Assert.That(entMan.HasComponent<KoronusTeleporterArrivalCooldownComponent>(passenger), Is.True,
                    "Принимающая платформа не должна немедленно отправить игрока обратно.");
                entMan.DeleteEntity(ruleEntity);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task FragmentedShuttleUsesEmergencyDepartureForEveryGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var orbital = await pair.CreateTestMap();
        var surface = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        var ruleEntity = EntityUid.Invalid;
        var trackedFragments = Array.Empty<EntityUid>();

        try
        {
            await server.WaitAssertion(() =>
            {
                ruleEntity = ConfigureTestSystem(entMan, orbital, surface, createDefaultPad: false);
                CreateLandingPad(entMan, surface.Grid.Owner, new Vector2i(-5, -3), 9, 5, "split-pad", 1);
                var shuttle = CreateShuttle(mapMan, mapSystem, entMan, orbital.MapId, new Vector2(120f, 0f), 5, 1);
                Assert.That(planetary.TryLand(shuttle, BodyId, "split-pad", out var failure), Is.True,
                    $"Посадка перед разрушением должна пройти: {failure}");

                var shuttleGrid = entMan.GetComponent<MapGridComponent>(shuttle);
                mapSystem.SetTile(shuttle, shuttleGrid, new Vector2i(2, 0), Tile.Empty);
            });

            await server.WaitRunTicks(5);
            await server.WaitAssertion(() =>
            {
                var rule = entMan.GetComponent<KoronusSectorRuleComponent>(ruleEntity);
                var session = rule.LandingSessions.Values.Single();
                trackedFragments = session.Fragments.Where(fragment => !entMan.Deleted(fragment)).ToArray();
                Assert.That(trackedFragments.Length, Is.GreaterThanOrEqualTo(2),
                    "GridSplitEvent должен зарегистрировать обе части разрушенного шаттла.");
            });

            await server.WaitRunTicks(40);
            await server.WaitAssertion(() =>
            {
                foreach (var fragment in trackedFragments.Where(fragment => !entMan.Deleted(fragment)))
                    Assert.That(xform.GetMapCoordinates(fragment).MapId, Is.EqualTo(orbital.MapId));

                var rule = entMan.GetComponent<KoronusSectorRuleComponent>(ruleEntity);
                Assert.That(rule.LandingSessions, Is.Empty);
                Assert.That(rule.LandingReservations, Is.Empty);
                entMan.DeleteEntity(ruleEntity);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static Vector2 GetCurrentCelestialPosition(
        IPrototypeManager prototypes,
        IGameTiming timing,
        string systemId,
        string bodyId)
    {
        var system = prototypes.Index<KoronusSystemPrototype>(systemId);
        var body = prototypes.Index<KoronusCelestialBodyPrototype>(bodyId);
        if (body.OrbitRadius <= 0f)
            return system.StellarCenter;

        var phase = (body.OrbitPhase + body.OrbitAngularSpeed * (float) timing.CurTime.TotalSeconds) *
                    MathF.PI / 180f;
        return system.StellarCenter +
               new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * body.OrbitRadius;
    }

    private static EntityUid ConfigureTestSystem(
        IEntityManager entMan,
        TestMapData orbital,
        TestMapData surface,
        bool createDefaultPad = true)
    {
        var ruleEntity = entMan.SpawnEntity("KoronusSectorBootstrap", new MapCoordinates(Vector2.Zero, MapId.Nullspace));
        var rule = entMan.GetComponent<KoronusSectorRuleComponent>(ruleEntity);
        var sector = entMan.System<KoronusSectorRuleSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        sector.RegisterSystemMap((ruleEntity, rule), SystemId, orbital.MapId);
        planetary.RegisterSurfaceMap((ruleEntity, rule), SurfaceId, SystemId, surface.MapId, surface.Grid);
        if (createDefaultPad)
            CreateLandingPad(entMan, surface.Grid.Owner, new Vector2i(-5, -3), 9, 5, SiteId, 0);

        return ruleEntity;
    }

    private static EntityUid ConfigureFoulstone(
        IEntityManager entMan,
        TestMapData orbital,
        TestMapData surface)
    {
        var ruleEntity = entMan.SpawnEntity("KoronusSectorBootstrap", new MapCoordinates(Vector2.Zero, MapId.Nullspace));
        var rule = entMan.GetComponent<KoronusSectorRuleComponent>(ruleEntity);
        var sector = entMan.System<KoronusSectorRuleSystem>();
        var planetary = entMan.System<KoronusPlanetarySystem>();
        sector.RegisterSystemMap((ruleEntity, rule), FoulstoneSystemId, orbital.MapId);
        planetary.RegisterSurfaceMap((ruleEntity, rule), FoulstoneSurfaceId, FoulstoneSystemId, surface.MapId, surface.Grid);
        CreateLandingPad(entMan, surface.Grid.Owner, new Vector2i(-5, -3), 9, 5, FoulstoneSiteId, 0);
        return ruleEntity;
    }

    private static EntityUid CreateLandingPad(
        IEntityManager entMan,
        EntityUid terrainGrid,
        Vector2i offset,
        int width,
        int height,
        string id,
        int parkingTime)
    {
        var map = entMan.System<SharedMapSystem>();
        var grid = entMan.GetComponent<MapGridComponent>(terrainGrid);
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                map.SetTile(terrainGrid, grid, offset + new Vector2i(x, y), new Tile(1));
                entMan.SpawnEntity(
                    "KoronusLandingPadTile",
                    new EntityCoordinates(terrainGrid, new Vector2(offset.X + x + 0.5f, offset.Y + y + 0.5f)));
            }
        }

        var consoleTile = new Vector2i(offset.X + width / 2, offset.Y + height);
        map.SetTile(terrainGrid, grid, consoleTile, new Tile(1));
        var console = entMan.SpawnEntity(
            "KoronusLandingPadConsole",
            new EntityCoordinates(terrainGrid, new Vector2(offset.X + width / 2f, offset.Y + height + 0.5f)));
        Assert.That(entMan.System<KoronusLandingPadSystem>()
            .TryConfigure(console, id, parkingTime, true, true, id), Is.True);
        SetAlwaysPowered(entMan, console);
        return console;
    }

    private static void SetAlwaysPowered(IEntityManager entMan, EntityUid entity)
    {
        var receiver = entMan.GetComponent<ApcPowerReceiverComponent>(entity);
        receiver.NeedsPower = false;
        receiver.Powered = true;
    }

    private static EntityUid CreateShuttle(
        IMapManager mapMan,
        SharedMapSystem mapSystem,
        IEntityManager entMan,
        MapId mapId,
        Vector2 position,
        int width,
        int height)
    {
        var grid = mapMan.CreateGridEntity(mapId);
        entMan.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, position);
        var tiles = new List<(Vector2i Index, Tile Tile)>(width * height);
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
        entMan.EnsureComponent<ShuttleComponent>(grid.Owner);
        return grid.Owner;
    }

    private sealed class Vector2Comparer : IEqualityComparer<Vector2>
    {
        public static readonly Vector2Comparer Instance = new();

        public bool Equals(Vector2 left, Vector2 right) => Vector2.DistanceSquared(left, right) < 0.0001f;

        public int GetHashCode(Vector2 value) => value.GetHashCode();
    }
}
