using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._Mono.Shuttles.Components;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Shuttles;
using Content.Shared.CCVar;
using Content.Tests;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Shuttle;

/// <summary>
/// Exercises the whole player autopilot path: console message, HTN steering, approach and final docking.
/// The test keeps a compact movement trace so an eventual failure can be diagnosed from CI output.
/// </summary>
public sealed class AutoDockIntegrationTest : ContentUnitTest
{
    private const float FlightDistance = 1000f;
    private const int SampleTicks = 30;
    private const int MaximumFlightTicks = 6000;

    [TestCase(4, 4, 0f, "короткий", 1800, TestName = "AutoDockShortHull")]
    [TestCase(48, 4, 0f, "широкий", 2100, TestName = "AutoDockWideHull")]
    [TestCase(4, 48, 0f, "длинный", 2100, TestName = "AutoDockLongHull")]
    [TestCase(4, 4, 90f, "развёрнутый", 2100, TestName = "AutoDockRotatedStation")]
    public async Task SelectedTargetAutoDocksFromOneThousandMetres(
        int width,
        int height,
        float stationRotationDegrees,
        string hullName,
        int performanceBudgetTicks)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var docking = entMan.System<DockingSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid stationGrid = default;
            EntityUid shuttleDock = default;
            EntityUid stationDock = default;
            EntityUid console = default;

            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);

                (shuttleGrid, shuttleDock, console) = CreateShuttle(
                    entMan,
                    mapMan,
                    mapSystem,
                    map.MapId,
                    width,
                    height,
                    Vector2.Zero);
                (stationGrid, stationDock) = CreateStation(
                    entMan,
                    mapMan,
                    mapSystem,
                    xform,
                    map.MapId,
                    new Vector2(0f, FlightDistance),
                    8,
                    8,
                    Angle.FromDegrees(stationRotationDegrees));

                var dockingConfig = docking.GetDockingConfig(shuttleGrid, stationGrid);
                Assert.That(dockingConfig, Is.Not.Null, "Тестовые шлюзы должны давать допустимую конфигурацию стыковки.");

                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotGridMessage
                {
                    TargetGrid = entMan.GetNetEntity(stationGrid),
                });

                Assert.That(entMan.TryGetComponent(console, out ShuttleConsoleAutoDockingComponent _), Is.True,
                    "Выбор станции с включённой автосстыковкой должен начать манёвр.");

                var htn = entMan.GetComponent<HTNComponent>(console);
                Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>("AutoDockTarget", out var approachTarget, entMan), Is.True);
                TestContext.Progress.WriteLine(
                    $"Конфигурация дока: финал {_xformToText(xform.ToMapCoordinates(dockingConfig!.Coordinates))}; " +
                    $"подход {_xformToText(xform.ToMapCoordinates(approachTarget))}; " +
                    $"шлюз шаттла {_xformToText(xform.GetMapCoordinates(shuttleDock))}; " +
                    $"шлюз станции {_xformToText(xform.GetMapCoordinates(stationDock))}.");
            });

            var trace = new List<string>();
            var docked = false;
            var destroyed = false;
            var maximumObservedSpeed = 0f;
            var completedTicks = MaximumFlightTicks;
            for (var elapsed = 0; elapsed < MaximumFlightTicks && !docked && !destroyed; elapsed += SampleTicks)
            {
                await server.WaitRunTicks(SampleTicks);
                completedTicks = elapsed + SampleTicks;
                await server.WaitPost(() =>
                {
                    if (!entMan.EntityExists(shuttleGrid))
                    {
                        destroyed = true;
                        trace.Add($"{elapsed + SampleTicks,4} тиков: шаттл уничтожен.");
                        return;
                    }

                    var shuttleDockComp = entMan.GetComponent<DockingComponent>(shuttleDock);
                    docked = shuttleDockComp.DockedWith == stationDock;
                    maximumObservedSpeed = MathF.Max(
                        maximumObservedSpeed,
                        entMan.GetComponent<PhysicsComponent>(shuttleGrid).LinearVelocity.Length());
                    trace.Add(GetTraceLine(entMan, xform, shuttleGrid, console, elapsed + SampleTicks));
                });
            }

            TestContext.Progress.WriteLine(
                $"Автосстыковка ({hullName}, {width}x{height}, {FlightDistance:0} м): " +
                $"{completedTicks} тиков, пик {maximumObservedSpeed:F2} м/с.{Environment.NewLine}" +
                string.Join(Environment.NewLine, trace));

            Assert.That(docked, Is.True,
                $"Шаттл {hullName} не состыковался за {MaximumFlightTicks} тиков.{Environment.NewLine}" +
                string.Join(Environment.NewLine, trace));
            Assert.That(completedTicks, Is.LessThanOrEqualTo(performanceBudgetTicks),
                $"Регрессия скорости алгоритма: бюджет для корпуса «{hullName}» — {performanceBudgetTicks} тиков.");
            Assert.That(maximumObservedSpeed, Is.LessThanOrEqualTo(26f),
                "Автостыковка не должна выходить за безопасный запас над крейсерским лимитом.");
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NormalAutopilotCoversOneThousandMetresWithinPerformanceBudget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid console = default;
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                (shuttleGrid, _, console) = CreateShuttle(
                    entMan,
                    mapMan,
                    mapSystem,
                    map.MapId,
                    4,
                    4,
                    Vector2.Zero);

                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotPositionMessage
                {
                    Coordinates = new MapCoordinates(new Vector2(0f, FlightDistance), map.MapId),
                    Angle = Angle.Zero,
                });

                var htn = entMan.GetComponent<HTNComponent>(console);
                Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>("Target", out _, entMan), Is.True);
            });

            const int performanceBudgetTicks = 1800;
            var completed = false;
            var completedTicks = performanceBudgetTicks;
            var maximumObservedSpeed = 0f;
            for (var elapsed = 0; elapsed < performanceBudgetTicks && !completed; elapsed += 10)
            {
                await server.WaitRunTicks(10);
                completedTicks = elapsed + 10;
                await server.WaitPost(() =>
                {
                    var htn = entMan.GetComponent<HTNComponent>(console);
                    completed = !htn.Blackboard.TryGetValue<EntityCoordinates>("Target", out _, entMan) &&
                                !entMan.HasComponent<ShipSteererComponent>(console);
                    maximumObservedSpeed = MathF.Max(
                        maximumObservedSpeed,
                        entMan.GetComponent<PhysicsComponent>(shuttleGrid).LinearVelocity.Length());
                });
            }

            var remainingDistance = Vector2.Distance(
                xform.GetMapCoordinates(shuttleGrid).Position,
                new Vector2(0f, FlightDistance));
            TestContext.Progress.WriteLine(
                $"Обычный автопилот, {FlightDistance:0} м: {completedTicks} тиков, " +
                $"пик {maximumObservedSpeed:F2} м/с, остаток {remainingDistance:F2} м.");

            Assert.That(completed, Is.True,
                $"Обычный автопилот не завершил перелёт за {performanceBudgetTicks} тиков.");
            Assert.That(remainingDistance, Is.LessThanOrEqualTo(40.5f),
                "Обычный автопилот не должен завершаться за пределами настроенного радиуса прибытия.");
            Assert.That(maximumObservedSpeed, Is.LessThanOrEqualTo(24.1f));
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [TestCase("/SharedMaps/_Mono/Shuttles/plutomkii.yml", "Pluto Mk. II")]
    [TestCase("/SharedMaps/_Mono/Shuttles/hammerhead.yml", "Hammerhead")]
    [TestCase("/SharedMaps/_Mono/Shuttles/USSP/tayfun.yml", "Tayfun")]
    public async Task RealShuttleSafelyDocksWithColossus(string shuttlePath, string shuttleName)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var loader = entMan.System<MapLoaderSystem>();
        var lookup = entMan.System<EntityLookupSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var docking = entMan.System<DockingSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid stationGrid = default;
            EntityUid console = default;
            var selectedPort = "без имени";
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.MapUid);
                var options = DeserializationOptions.Default with { InitializeMaps = true };
                Assert.That(loader.TryLoadMap(
                    new ResPath("/Maps/_Mono/Outpost/colossus_central.yml"),
                    out var loadedMap,
                    out var stationGrids,
                    options), Is.True);
                Assert.That(stationGrids, Is.Not.Empty);
                Assert.That(loader.TryLoadGrid(
                    loadedMap!.Value.Comp.MapId,
                    new ResPath(shuttlePath),
                    out var loadedShuttle,
                    options), Is.True);
                stationGrid = stationGrids!.First().Owner;
                shuttleGrid = loadedShuttle!.Value.Owner;

                xform.SetLocalPosition(stationGrid, Vector2.Zero);
                xform.SetWorldRotation(stationGrid, Angle.Zero);
                // Восточный подлёт воспроизводит C-сектор Colossus с изображения: ближайший
                // геометрически порт не всегда имеет прямой свободный коридор.
                xform.SetLocalPosition(shuttleGrid, new Vector2(135f, 25f));
                xform.SetWorldRotation(shuttleGrid, Angle.Zero);

                var consoles = new HashSet<Entity<ShuttleConsoleComponent>>();
                lookup.GetChildEntities(shuttleGrid, consoles);
                console = consoles
                    .Where(candidate => entMan.HasComponent<HTNComponent>(candidate))
                    .Select(candidate => candidate.Owner)
                    .FirstOrDefault();
                Assert.That(console, Is.Not.EqualTo(EntityUid.Invalid),
                    $"На реальном корабле {shuttleName} не найдена консоль с автопилотом.");

                if (entMan.TryGetComponent(console, out ApcPowerReceiverComponent receiver))
                {
                    receiver.NeedsPower = false;
                    receiver.Powered = true;
                }
            });

            // Карта должна закончить MapInit и пересчитать тягу от реальных двигателей.
            await server.WaitRunTicks(10);
            await server.WaitAssertion(() =>
            {
                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotGridMessage
                {
                    TargetGrid = entMan.GetNetEntity(stationGrid),
                });

                Assert.That(entMan.TryGetComponent(console, out ShuttleConsoleAutoDockingComponent autoDock), Is.True,
                    $"Для {shuttleName} не найден безопасный порт Colossus.");
                selectedPort = entMan.GetComponent<DockingComponent>(autoDock.TargetDock).Name;
            });

            var docked = false;
            var failed = false;
            var completedTicks = 0;
            var peakSpeed = 0f;
            for (var elapsed = 0; elapsed < 6000 && !docked && !failed; elapsed += 10)
            {
                await server.WaitRunTicks(10);
                completedTicks = elapsed + 10;
                await server.WaitPost(() =>
                {
                    peakSpeed = MathF.Max(
                        peakSpeed,
                        entMan.GetComponent<PhysicsComponent>(shuttleGrid).LinearVelocity.Length());
                    docked = docking.GetDocks(shuttleGrid).Any(port =>
                        port.Comp.DockedWith is { } other && TransformOnGrid(entMan, other, stationGrid));
                    failed = !docked && !entMan.HasComponent<ShuttleConsoleAutoDockingComponent>(console);
                });
            }

            TestContext.Progress.WriteLine(
                $"Реальная стыковка {shuttleName} -> Colossus/{selectedPort}: " +
                $"{completedTicks} тиков, пик {peakSpeed:F2} м/с.");
            Assert.That(failed, Is.False,
                $"{shuttleName} прекратил манёвр после столкновения или потери безопасного коридора.");
            Assert.That(docked, Is.True,
                $"{shuttleName} не состыковался с реальной геометрией Colossus.");
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task RealTumourNearSec2RejectsUnsafeDockingEnvelope()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var loader = entMan.System<MapLoaderSystem>();
        var lookup = entMan.System<EntityLookupSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var docking = entMan.System<DockingSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid stationGrid = default;
            EntityUid console = default;
            EntityUid sec2 = default;
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.MapUid);
                var options = DeserializationOptions.Default with { InitializeMaps = true };
                Assert.That(loader.TryLoadMap(
                    new ResPath("/Maps/_Mono/Outpost/colossus_central.yml"),
                    out var loadedMap,
                    out var stationGrids,
                    options), Is.True);
                Assert.That(loader.TryLoadGrid(
                    loadedMap!.Value.Comp.MapId,
                    new ResPath("/SharedMaps/_Mono/Shuttles/Nfsd/tumour.yml"),
                    out var loadedShuttle,
                    options), Is.True);

                stationGrid = stationGrids!.First().Owner;
                shuttleGrid = loadedShuttle!.Value.Owner;
                xform.SetLocalPosition(stationGrid, Vector2.Zero);
                xform.SetWorldRotation(stationGrid, Angle.Zero);

                sec2 = docking.GetDocks(stationGrid)
                    .Single(port => port.Comp.Name == "SEC-2")
                    .Owner;
                var sec2Position = xform.GetMapCoordinates(sec2).Position;
                var outward = xform.GetWorldRotation(sec2).RotateVec(new Vector2(0f, -1f));

                // This is the real close-range layout from the reported case: the Tumour is
                // upright immediately outside SEC-2. Its port is nearby, but the complete hull
                // cannot use that docking envelope without intersecting Colossus.
                xform.SetLocalPosition(shuttleGrid, sec2Position + outward * 14f);
                xform.SetWorldRotation(shuttleGrid, Angle.Zero);

                var consoles = new HashSet<Entity<ShuttleConsoleComponent>>();
                lookup.GetChildEntities(shuttleGrid, consoles);
                console = consoles
                    .Where(candidate => entMan.HasComponent<HTNComponent>(candidate))
                    .Select(candidate => candidate.Owner)
                    .FirstOrDefault();
                Assert.That(console, Is.Not.EqualTo(EntityUid.Invalid));

                if (entMan.TryGetComponent(console, out ApcPowerReceiverComponent receiver))
                {
                    receiver.NeedsPower = false;
                    receiver.Powered = true;
                }
            });

            await server.WaitRunTicks(10);
            await server.WaitAssertion(() =>
            {
                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotGridMessage
                {
                    TargetGrid = entMan.GetNetEntity(stationGrid),
                });

                if (entMan.TryGetComponent(console, out ShuttleConsoleAutoDockingComponent autoDock))
                {
                    Assert.That(autoDock.TargetDock, Is.Not.EqualTo(sec2),
                        "Автостыковка не должна выбирать SEC-2, когда туда не помещается полный корпус Tumour.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    private static bool TransformOnGrid(IEntityManager entMan, EntityUid entity, EntityUid grid)
    {
        return entMan.GetComponent<TransformComponent>(entity).GridUid == grid;
    }

    [Test]
    public async Task TerminalAutoDockClosesTheLastMetresWithoutPositionJump()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var docking = entMan.System<DockingSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid stationDock = default;
            EntityUid shuttleDock = default;
            EntityUid console = default;
            var lastPosition = Vector2.Zero;

            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                (shuttleGrid, shuttleDock, console) = CreateShuttle(
                    entMan,
                    mapMan,
                    mapSystem,
                    map.MapId,
                    4,
                    4,
                    Vector2.Zero);
                (_, stationDock) = CreateStation(
                    entMan,
                    mapMan,
                    mapSystem,
                    xform,
                    map.MapId,
                    new Vector2(0f, 110f),
                    8,
                    8);

                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutoDockRequestMessage());
                lastPosition = xform.GetMapCoordinates(shuttleGrid).Position;
            });

            var enteredTerminalPhase = false;
            var docked = false;
            var maximumTerminalStep = 0f;
            for (var tick = 0; tick < 3000 && !docked; tick++)
            {
                await server.WaitRunTicks(1);
                await server.WaitPost(() =>
                {
                    var position = xform.GetMapCoordinates(shuttleGrid).Position;
                    var inTerminalPhase = entMan.TryGetComponent(console, out ShuttleConsoleAutoDockingComponent autoDock) &&
                                          autoDock.Phase == AutoDockPhase.TerminalApproach;
                    if (enteredTerminalPhase || inTerminalPhase)
                        maximumTerminalStep = MathF.Max(maximumTerminalStep, Vector2.Distance(position, lastPosition));

                    enteredTerminalPhase |= inTerminalPhase;
                    lastPosition = position;
                    docked = entMan.GetComponent<DockingComponent>(shuttleDock).DockedWith == stationDock;
                });
            }

            Assert.That(enteredTerminalPhase, Is.True,
                "Сценарий должен войти в отдельную физическую фазу финальной стыковки.");
            Assert.That(docked, Is.True, "Шаттл должен состыковаться после физического терминального сближения.");
            Assert.That(maximumTerminalStep, Is.LessThanOrEqualTo(0.15f),
                "В терминальной фазе не допускается скачок координат к стыковочному шлюзу.");
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task AutoDockMatchesTheVelocityOfAMovingStation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid stationGrid = default;
            EntityUid shuttleDock = default;
            EntityUid stationDock = default;
            EntityUid console = default;

            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                (shuttleGrid, shuttleDock, console) = CreateShuttle(
                    entMan,
                    mapMan,
                    mapSystem,
                    map.MapId,
                    4,
                    4,
                    Vector2.Zero);
                (stationGrid, stationDock) = CreateStation(
                    entMan,
                    mapMan,
                    mapSystem,
                    xform,
                    map.MapId,
                    new Vector2(0f, 110f),
                    8,
                    8);

                var stationBody = entMan.GetComponent<PhysicsComponent>(stationGrid);
                physics.SetLinearVelocity(stationGrid, new Vector2(0.25f, 0.15f), body: stationBody);

                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutoDockRequestMessage());
            });

            var docked = false;
            for (var elapsed = 0; elapsed < 3000 && !docked; elapsed += 10)
            {
                await server.WaitRunTicks(10);
                await server.WaitPost(() =>
                {
                    docked = entMan.GetComponent<DockingComponent>(shuttleDock).DockedWith == stationDock;
                });
            }

            Assert.That(docked, Is.True,
                "Автостыковка должна выйти на скорость движущейся станции и состыковаться.");
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NearestAutoDockUsesNearbyPortInsteadOfLargeStationCentre()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();

        try
        {
            EntityUid shuttleGrid = default;
            EntityUid stationGrid = default;
            EntityUid console = default;

            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                (shuttleGrid, _, console) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 4, 4, Vector2.Zero);
                (stationGrid, _) = CreateStation(
                    entMan,
                    mapMan,
                    mapSystem,
                    xform,
                    map.MapId,
                    new Vector2(0f, 250f),
                    800,
                    4);

                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutoDockRequestMessage());

                Assert.That(entMan.TryGetComponent(console, out ShuttleConsoleAutoDockingComponent autoDock), Is.True,
                    "Станция должна быть найдена по шлюзу, расположенному в 250 м от шаттла.");
                Assert.That(autoDock.TargetGrid, Is.EqualTo(stationGrid));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task AutoDockSelectsClosestCompatiblePortOnSelectedStation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                var (shuttleGrid, _, console) = CreateShuttle(
                    entMan,
                    mapMan,
                    mapSystem,
                    map.MapId,
                    4,
                    4,
                    new Vector2(40f, 0f));
                var (stationGrid, distantDock) = CreateStation(
                    entMan,
                    mapMan,
                    mapSystem,
                    xform,
                    map.MapId,
                    new Vector2(0f, 150f),
                    64,
                    4);
                var nearbyDock = entMan.SpawnEntity(
                    "AirlockShuttle",
                    new EntityCoordinates(stationGrid, 40.5f, 0.5f));

                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotGridMessage
                {
                    TargetGrid = entMan.GetNetEntity(stationGrid),
                });

                Assert.That(entMan.TryGetComponent(console, out ShuttleConsoleAutoDockingComponent autoDock), Is.True);
                Assert.That(autoDock.TargetDock, Is.EqualTo(nearbyDock),
                    "Для явно выбранной станции должен выбираться ближайший совместимый шлюз, а не первый в списке.");
                Assert.That(autoDock.TargetDock, Is.Not.EqualTo(distantDock));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task SelectedGridWithoutAutoDockUsesMovingAnchor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                var (shuttleGrid, _, console) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 8, 8, Vector2.Zero);
                var (stationGrid, _) = CreateStation(
                    entMan,
                    mapMan,
                    mapSystem,
                    xform,
                    map.MapId,
                    new Vector2(0f, 120f),
                    24,
                    24,
                    Angle.FromDegrees(90f));

                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotGridMessage
                {
                    TargetGrid = entMan.GetNetEntity(stationGrid),
                });

                Assert.That(entMan.HasComponent<ShuttleConsoleAutoDockingComponent>(console), Is.False,
                    "Выбранный грид не должен запускать автостыковку, пока она выключена в DOCK.");

                var htn = entMan.GetComponent<HTNComponent>(console);
                Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>("Target", out var target, entMan), Is.True);
                Assert.That(target.EntityId, Is.EqualTo(stationGrid),
                    "Цель должна храниться в локальных координатах станции, чтобы следовать за ней.");

                var stationBounds = entMan.GetComponent<MapGridComponent>(stationGrid).LocalAABB;
                Assert.That(
                    target.Position.X < stationBounds.Left ||
                    target.Position.X > stationBounds.Right ||
                    target.Position.Y < stationBounds.Bottom ||
                    target.Position.Y > stationBounds.Top,
                    Is.True,
                    "Обычный автопилот должен держать anchor снаружи корпуса цели.");

                var targetBeforeMove = xform.ToMapCoordinates(target).Position;
                var stationMove = new Vector2(17f, -9f);
                xform.SetLocalPosition(
                    stationGrid,
                    entMan.GetComponent<TransformComponent>(stationGrid).LocalPosition + stationMove);
                var targetAfterMove = xform.ToMapCoordinates(target).Position;
                Assert.That(Vector2.Distance(targetAfterMove, targetBeforeMove + stationMove), Is.LessThan(0.001f),
                    "Anchor автопилота должен перемещаться вместе со станцией.");
                Assert.That(shuttleGrid, Is.Not.EqualTo(stationGrid));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task FreeAutopilotPointDoesNotStartAutoDock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                var (shuttleGrid, _, console) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 8, 8, Vector2.Zero);
                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                var requestedPosition = new Vector2(137f, -42f);

                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotPositionMessage
                {
                    Coordinates = new MapCoordinates(requestedPosition, map.MapId),
                    Angle = Angle.Zero,
                });

                Assert.That(entMan.HasComponent<ShuttleConsoleAutoDockingComponent>(console), Is.False,
                    "Свободная точка NAV всегда должна оставаться обычной целью автопилота.");
                var htn = entMan.GetComponent<HTNComponent>(console);
                Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>("Target", out var target, entMan), Is.True);
                Assert.That(Vector2.Distance(xform.ToMapCoordinates(target).Position, requestedPosition), Is.LessThan(0.001f));
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task AutopilotUndocksEveryActivePortBeforeStartingFlight()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var docking = entMan.System<DockingSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                var (shuttleGrid, _, console) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 8, 4, Vector2.Zero);
                entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(shuttleGrid, 2.5f, 0.5f));
                entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(shuttleGrid, 4.5f, 0.5f));

                var (stationGrid, _) = CreateStation(
                    entMan,
                    mapMan,
                    mapSystem,
                    xform,
                    map.MapId,
                    new Vector2(0f, 120f),
                    8,
                    4);
                entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(stationGrid, 2.5f, 0.5f));
                entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(stationGrid, 4.5f, 0.5f));

                var dockConfiguration = docking.GetDockingConfig(shuttleGrid, stationGrid);
                Assert.That(dockConfiguration, Is.Not.Null);
                Assert.That(dockConfiguration!.Docks.Count, Is.EqualTo(3));
                foreach (var dockPair in dockConfiguration.Docks)
                {
                    docking.Dock(
                        (dockPair.DockAUid, entMan.GetComponent<DockingComponent>(dockPair.DockAUid)),
                        (dockPair.DockBUid, entMan.GetComponent<DockingComponent>(dockPair.DockBUid)));
                }

                Assert.That(
                    dockConfiguration.Docks.All(pair =>
                        entMan.GetComponent<DockingComponent>(pair.DockAUid).DockedWith == pair.DockBUid),
                    Is.True,
                    "Предусловие: все три порта должны быть состыкованы.");

                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotPositionMessage
                {
                    Coordinates = new MapCoordinates(new Vector2(160f, 40f), map.MapId),
                    Angle = Angle.Zero,
                });

                var htn = entMan.GetComponent<HTNComponent>(console);
                Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>("Target", out _, entMan), Is.True,
                    "После штатной отстыковки команда полёта должна быть принята.");
                Assert.That(
                    dockConfiguration.Docks.All(pair =>
                        entMan.GetComponent<DockingComponent>(pair.DockAUid).DockedWith == null &&
                        entMan.GetComponent<DockingComponent>(pair.DockBUid).DockedWith == null),
                    Is.True,
                    "Автопилот должен освободить все активные порты шаттла одним отправлением, а не оставить часть ряда состыкованной.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task FreeAutopilotTurnsBeforeApplyingTravelThrust()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid console = default;
            var initialPosition = Vector2.Zero;
            var targetPosition = new Vector2(0f, 200f);
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                (shuttleGrid, _, console) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 4, 4, Vector2.Zero);
                xform.SetWorldRotation(shuttleGrid, Angle.FromDegrees(90f));
                initialPosition = xform.GetMapCoordinates(shuttleGrid).Position;

                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotPositionMessage
                {
                    Coordinates = new MapCoordinates(targetPosition, map.MapId),
                    Angle = Angle.Zero,
                });
            });

            var steeringStarted = false;
            var aligned = false;
            var movedAfterAlignment = false;
            var maximumSpeedBeforeAlignment = 0f;
            var maximumLateralOffset = 0f;
            for (var tick = 0; tick < 1800 && !movedAfterAlignment; tick++)
            {
                await server.WaitRunTicks(1);
                await server.WaitPost(() =>
                {
                    if (!entMan.TryGetComponent(console, out ShipSteererComponent steerer))
                        return;

                    steeringStarted = true;
                    Assert.That(steerer.ForwardFlight, Is.True,
                        "Конфигурация пользовательского автопилота должна включать курсовой режим.");

                    var position = xform.GetMapCoordinates(shuttleGrid).Position;
                    maximumLateralOffset = MathF.Max(maximumLateralOffset, MathF.Abs(position.X - initialPosition.X));
                    var desiredCourse = (targetPosition - position).ToWorldAngle();
                    var hullForward = xform.GetWorldRotation(shuttleGrid) + new Angle(Math.PI);
                    var headingError = MathF.Abs((float) ShipSteeringSystem.ShortestAngleDistance(hullForward, desiredCourse).Theta);
                    var speed = entMan.GetComponent<PhysicsComponent>(shuttleGrid).LinearVelocity.Length();
                    if (headingError > steerer.ForwardFlightExitAngle)
                        maximumSpeedBeforeAlignment = MathF.Max(maximumSpeedBeforeAlignment, speed);
                    else
                        aligned = true;

                    if (aligned && Vector2.Distance(position, initialPosition) > 5f)
                        movedAfterAlignment = true;
                });
            }

            Assert.That(steeringStarted, Is.True, "HTN должен передать приказ в ShipSteeringSystem.");
            Assert.That(aligned, Is.True, "Шаттл с разворотом на 90 градусов должен сначала выровнять курс.");
            Assert.That(maximumSpeedBeforeAlignment, Is.LessThanOrEqualTo(0.3f),
                "До выравнивания корпуса автопилот не должен набирать боковую скорость к цели.");
            Assert.That(movedAfterAlignment, Is.True,
                "После выравнивания автопилот должен продолжить обычный полёт.");
            Assert.That(maximumLateralOffset, Is.LessThanOrEqualTo(5f),
                "Без препятствий автопилот должен продолжить движение по прямому курсу, а не уходить боком.");
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [TestCase(false, TestName = "AutopilotAvoidsAsteroidGridOnDirectRoute")]
    [TestCase(true, TestName = "AutopilotAvoidsOtherShuttleOnDirectRoute")]
    public async Task FreeAutopilotAvoidsObstacleOnDirectRoute(bool useShuttleObstacle)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid obstacleGrid = default;
            EntityUid console = default;
            var targetPosition = new Vector2(2f, 200f);
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                (shuttleGrid, _, console) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 4, 4, Vector2.Zero);

                if (useShuttleObstacle)
                {
                    (obstacleGrid, _, _) = CreateShuttle(
                        entMan,
                        mapMan,
                        mapSystem,
                        map.MapId,
                        20,
                        8,
                        new Vector2(-10f, 85f));
                    physics.SetLinearVelocity(
                        obstacleGrid,
                        new Vector2(0.5f, 0f),
                        body: entMan.GetComponent<PhysicsComponent>(obstacleGrid));
                }
                else
                {
                    obstacleGrid = CreateObstacleGrid(entMan, mapMan, mapSystem, map.MapId, new Vector2(-10f, 85f), 20, 8);
                }

                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotPositionMessage
                {
                    Coordinates = new MapCoordinates(targetPosition, map.MapId),
                    Angle = Angle.Zero,
                });
            });

            var maximumSideOffset = 0f;
            var passedObstacle = false;
            var destroyed = false;
            var hullsIntersected = false;
            var lastPosition = Vector2.Zero;
            var lastSpeed = 0f;
            var trace = new List<string>();
            for (var tick = 0; tick < 1800 && !passedObstacle && !destroyed; tick++)
            {
                await server.WaitRunTicks(1);
                await server.WaitPost(() =>
                {
                    destroyed = !entMan.EntityExists(shuttleGrid);
                    if (destroyed)
                        return;

                    var position = xform.GetMapCoordinates(shuttleGrid).Position;
                    lastPosition = position;
                    maximumSideOffset = MathF.Max(maximumSideOffset, MathF.Abs(position.X - targetPosition.X));
                    lastSpeed = entMan.GetComponent<PhysicsComponent>(shuttleGrid).LinearVelocity.Length();
                    hullsIntersected |= physics.GetWorldAABB(shuttleGrid).Intersects(physics.GetWorldAABB(obstacleGrid));
                    if (tick % 60 == 0)
                    {
                        var waypoint = entMan.TryGetComponent(console, out ShipSteererComponent waypointSteerer) &&
                                       waypointSteerer.AvoidanceWaypoint is { } activeWaypoint
                            ? activeWaypoint.Position.ToString()
                            : "нет";
                        trace.Add(GetTraceLine(entMan, xform, shuttleGrid, console, tick) +
                                  $"; waypoint {waypoint}");
                    }
                    passedObstacle = position.Y > 125f;
                });
            }

            Assert.That(destroyed, Is.False, "Шаттл не должен быть уничтожен препятствием на прямом маршруте.");
            Assert.That(hullsIntersected, Is.False,
                "При обходе прямого препятствия корпуса шаттлов не должны пересекаться.");
            Assert.That(passedObstacle, Is.True,
                "Автопилот должен обойти препятствие и продолжить маршрут, а не остановиться перед ним. " +
                $"Последняя позиция: {lastPosition}; скорость: {lastSpeed:F3}; боковое отклонение: {maximumSideOffset:F1}." +
                Environment.NewLine + string.Join(Environment.NewLine, trace));
            Assert.That(maximumSideOffset, Is.GreaterThan(12f),
                "Маршрут должен отклониться от прямой достаточно далеко, чтобы обойти корпус астероида или другого шаттла.");
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task AutoDockKeepsAllAlignedPortsForTheSelectedConfiguration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var docking = entMan.System<DockingSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            List<(EntityUid ShuttleDock, EntityUid StationDock)> dockPairs = new();
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                var (shuttleGrid, _, console) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 8, 4, Vector2.Zero);
                entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(shuttleGrid, 2.5f, 0.5f));
                entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(shuttleGrid, 4.5f, 0.5f));

                var (stationGrid, _) = CreateStation(
                    entMan,
                    mapMan,
                    mapSystem,
                    xform,
                    map.MapId,
                    new Vector2(0f, 120f),
                    8,
                    4);
                entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(stationGrid, 2.5f, 0.5f));
                entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(stationGrid, 4.5f, 0.5f));

                var standardConfiguration = docking.GetDockingConfig(shuttleGrid, stationGrid);
                Assert.That(standardConfiguration, Is.Not.Null);
                Assert.That(standardConfiguration!.Docks.Count, Is.EqualTo(3),
                    "Тестовая геометрия должна давать стандартной системе стыковки все три совмещённые пары.");

                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotGridMessage
                {
                    TargetGrid = entMan.GetNetEntity(stationGrid),
                });

                Assert.That(entMan.TryGetComponent(console, out ShuttleConsoleAutoDockingComponent autoDock), Is.True);
                Assert.That(autoDock.Configuration, Is.Not.Null);
                Assert.That(autoDock.Configuration!.Docks.Count, Is.EqualTo(3),
                    "После выбора ближайшей пары конфигурация должна сохранить все три совмещённые пары шлюзов.");

                dockPairs = autoDock.Configuration.Docks
                    .Select(pair => (pair.DockAUid, pair.DockBUid))
                    .ToList();
            });

            var allDocked = false;
            for (var elapsed = 0; elapsed < 3000 && !allDocked; elapsed += 10)
            {
                await server.WaitRunTicks(10);
                await server.WaitPost(() =>
                {
                    allDocked = dockPairs.All(pair =>
                        entMan.GetComponent<DockingComponent>(pair.ShuttleDock).DockedWith == pair.StationDock);
                });
            }

            Assert.That(allDocked, Is.True,
                "После терминальной фазы должны защёлкнуться все пары шлюзов выбранной конфигурации, а не только первая.");
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task ReplacingNormalAutopilotDoesNotCancelNewAutoDockOrder()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid stationGrid = default;
            EntityUid console = default;
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                (shuttleGrid, _, console) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 4, 4, Vector2.Zero);
                (stationGrid, _) = CreateStation(entMan, mapMan, mapSystem, xform, map.MapId, new Vector2(0f, 120f), 8, 8);

                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotPositionMessage
                {
                    Coordinates = new MapCoordinates(new Vector2(180f, 0f), map.MapId),
                    Angle = Angle.Zero,
                });
            });

            await server.WaitRunTicks(5);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<ShipSteererComponent>(console), Is.True,
                    "Обычный автопилот должен успеть запустить HTN-руление.");

                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotGridMessage
                {
                    TargetGrid = entMan.GetNetEntity(stationGrid),
                });
                Assert.That(entMan.HasComponent<ShuttleConsoleAutoDockingComponent>(console), Is.True);
            });

            // Старый ShipMoveToOperator завершает план асинхронно. Его SteeringDoneEvent не должен
            // быть принят за результат только что установленного приказа AutoDockTarget.
            await server.WaitRunTicks(10);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<ShuttleConsoleAutoDockingComponent>(console), Is.True,
                    "Завершение старого обычного автопилота не должно отменять новую автостыковку.");
                var htn = entMan.GetComponent<HTNComponent>(console);
                Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>("AutoDockTarget", out _, entMan), Is.True);
            });
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NewConsoleOrderReplacesOtherConsoleAutopilotOnSameShuttle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid shuttleGrid = default;
            EntityUid firstConsole = default;
            EntityUid secondConsole = default;
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                (shuttleGrid, _, firstConsole) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 4, 4, Vector2.Zero);
                secondConsole = entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(shuttleGrid, 2.5f, 1.5f));
                if (entMan.TryGetComponent(secondConsole, out ApcPowerReceiverComponent receiver))
                {
                    receiver.NeedsPower = false;
                    receiver.Powered = true;
                }

                entMan.EventBus.RaiseLocalEvent(firstConsole, new ShuttleConsoleAutopilotPositionMessage
                {
                    Coordinates = new MapCoordinates(new Vector2(0f, 200f), map.MapId),
                    Angle = Angle.Zero,
                });
            });

            await server.WaitRunTicks(5);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<ShipSteererComponent>(firstConsole), Is.True);
                entMan.EventBus.RaiseLocalEvent(secondConsole, new ShuttleConsoleAutopilotPositionMessage
                {
                    Coordinates = new MapCoordinates(new Vector2(200f, 0f), map.MapId),
                    Angle = Angle.Zero,
                });

                var secondHtn = entMan.GetComponent<HTNComponent>(secondConsole);
                Assert.That(secondHtn.Blackboard.TryGetValue<EntityCoordinates>("Target", out _, entMan), Is.True,
                    "Новый приказ должен попасть в blackboard второй консоли.");
            });

            await server.WaitRunTicks(5);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<ShipSteererComponent>(firstConsole), Is.False,
                    "Старый пульт не должен продолжать подавать тягу после приказа с другого пульта.");
                Assert.That(entMan.HasComponent<ShipSteererComponent>(secondConsole), Is.True);
                var firstHtn = entMan.GetComponent<HTNComponent>(firstConsole);
                Assert.That(firstHtn.Blackboard.TryGetValue<EntityCoordinates>("Target", out _, entMan), Is.False);
                Assert.That(entMan.GetComponent<PilotedShuttleComponent>(shuttleGrid).ActiveSources, Is.EqualTo(1),
                    "На шаттле должен остаться ровно один автономный источник управления.");
            });
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task ConcurrentAutoDocksCompleteIndependently()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var pauseNpcs = config.GetCVar(CCVars.NPCPauseWhenNoPlayersInRange);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));

            EntityUid firstShuttleDock = default;
            EntityUid firstStationDock = default;
            EntityUid secondShuttleDock = default;
            EntityUid secondStationDock = default;
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);

                var firstShuttle = CreateShuttle(
                    entMan, mapMan, mapSystem, map.MapId, 4, 4, new Vector2(-300f, 0f));
                var firstStation = CreateStation(
                    entMan, mapMan, mapSystem, xform, map.MapId, new Vector2(-300f, 160f), 8, 8);
                var secondShuttle = CreateShuttle(
                    entMan, mapMan, mapSystem, map.MapId, 4, 4, new Vector2(300f, 0f));
                var secondStation = CreateStation(
                    entMan, mapMan, mapSystem, xform, map.MapId, new Vector2(300f, 160f), 8, 8);

                firstShuttleDock = firstShuttle.Dock;
                firstStationDock = firstStation.Dock;
                secondShuttleDock = secondShuttle.Dock;
                secondStationDock = secondStation.Dock;

                entMan.EnsureComponent<AutoDockComponent>(firstShuttle.Grid).Enabled = true;
                entMan.EnsureComponent<AutoDockComponent>(secondShuttle.Grid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(firstShuttle.Console, new ShuttleConsoleAutopilotGridMessage
                {
                    TargetGrid = entMan.GetNetEntity(firstStation.Grid),
                });
                entMan.EventBus.RaiseLocalEvent(secondShuttle.Console, new ShuttleConsoleAutopilotGridMessage
                {
                    TargetGrid = entMan.GetNetEntity(secondStation.Grid),
                });
            });

            var bothDocked = false;
            for (var elapsed = 0; elapsed < 3000 && !bothDocked; elapsed += 10)
            {
                await server.WaitRunTicks(10);
                await server.WaitPost(() =>
                {
                    bothDocked = entMan.GetComponent<DockingComponent>(firstShuttleDock).DockedWith == firstStationDock &&
                                 entMan.GetComponent<DockingComponent>(secondShuttleDock).DockedWith == secondStationDock;
                });
            }

            Assert.That(bothDocked, Is.True,
                "Две одновременные HTN-автостыковки не должны подавлять события завершения друг друга.");
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, pauseNpcs));
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task AutoDockIgnoresPortOutsideThreeHundredMetres()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.ResolveDependency<IEntityManager>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var xform = entMan.System<SharedTransformSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                entMan.DeleteEntity(map.Grid);
                var (shuttleGrid, _, console) = CreateShuttle(entMan, mapMan, mapSystem, map.MapId, 4, 4, Vector2.Zero);
                CreateStation(entMan, mapMan, mapSystem, xform, map.MapId, new Vector2(0f, 301f), 8, 8);

                entMan.EnsureComponent<AutoDockComponent>(shuttleGrid).Enabled = true;
                entMan.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutoDockRequestMessage());

                Assert.That(entMan.HasComponent<ShuttleConsoleAutoDockingComponent>(console), Is.False,
                    "Шлюз дальше 300 м не должен быть выбран для автоматической стыковки.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static (EntityUid Grid, EntityUid Dock, EntityUid Console) CreateShuttle(
        IEntityManager entMan,
        IMapManager mapMan,
        SharedMapSystem mapSystem,
        MapId mapId,
        int width,
        int height,
        Vector2 position)
    {
        var grid = mapMan.CreateGridEntity(mapId);
        entMan.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, position);
        mapSystem.SetTiles(grid.Owner, grid.Comp, MakeTiles(width, height));

        var shuttle = entMan.EnsureComponent<ShuttleComponent>(grid.Owner);
        for (var i = 0; i < shuttle.LinearThrust.Length; i++)
        {
            shuttle.LinearThrust[i] = 20000f;
            shuttle.BaseLinearThrust[i] = shuttle.LinearThrust[i];
        }

        shuttle.AngularThrust = MathF.Max(4000f, (width + height) * 500f);

        var dock = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
        var console = entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid.Owner, 1.5f, 1.5f));
        if (entMan.TryGetComponent(console, out ApcPowerReceiverComponent receiver))
        {
            receiver.NeedsPower = false;
            receiver.Powered = true;
        }

        return (grid.Owner, dock, console);
    }

    private static (EntityUid Grid, EntityUid Dock) CreateStation(
        IEntityManager entMan,
        IMapManager mapMan,
        SharedMapSystem mapSystem,
        SharedTransformSystem xform,
        MapId mapId,
        Vector2 position,
        int width,
        int height,
        Angle rotation = default)
    {
        var grid = mapMan.CreateGridEntity(mapId);
        xform.SetLocalPosition(grid.Owner, position);
        xform.SetWorldRotation(grid.Owner, rotation);
        mapSystem.SetTiles(grid.Owner, grid.Comp, MakeTiles(width, height));
        var dock = entMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
        return (grid.Owner, dock);
    }

    private static EntityUid CreateObstacleGrid(
        IEntityManager entMan,
        IMapManager mapMan,
        SharedMapSystem mapSystem,
        MapId mapId,
        Vector2 position,
        int width,
        int height)
    {
        var grid = mapMan.CreateGridEntity(mapId);
        entMan.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, position);
        mapSystem.SetTiles(grid.Owner, grid.Comp, MakeTiles(width, height));
        return grid.Owner;
    }

    private static List<(Vector2i Index, Tile Tile)> MakeTiles(int width, int height)
    {
        var tiles = new List<(Vector2i Index, Tile Tile)>(width * height);
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        return tiles;
    }

    private static string GetTraceLine(
        IEntityManager entMan,
        SharedTransformSystem xform,
        EntityUid shuttleGrid,
        EntityUid console,
        int ticks)
    {
        var position = xform.GetMapCoordinates(shuttleGrid).Position;
        var body = entMan.GetComponent<PhysicsComponent>(shuttleGrid);
        var shuttle = entMan.GetComponent<ShuttleComponent>(shuttleGrid);
        var piloted = entMan.TryGetComponent(shuttleGrid, out PilotedShuttleComponent pilotState)
            ? $"{pilotState.ActiveSources}/{pilotState.InputSources.Count}"
            : "нет";
        var steering = entMan.TryGetComponent(console, out ShipSteererComponent steerer)
            ? steerer.Status.ToString()
            : "нет";
        var phase = entMan.TryGetComponent(console, out ShuttleConsoleAutoDockingComponent autoDock)
            ? autoDock.Phase.ToString()
            : "завершено";
        var target = entMan.TryGetComponent(console, out ShipSteererComponent targetSteerer)
            ? xform.ToMapCoordinates(targetSteerer.Coordinates).Position
            : Vector2.Zero;
        var compensation = entMan.TryGetComponent(console, out ShipSteererComponent compensationSteerer)
            ? compensationSteerer.RotationCompensation
            : 0f;
        var rotation = xform.GetWorldRotation(shuttleGrid);

        return $"{ticks,4} тиков: позиция {position.X:F1}, {position.Y:F1}; " +
               $"скорость {body.LinearVelocity.X:F2}, {body.LinearVelocity.Y:F2} ({body.LinearVelocity.Length():F2}); " +
               $"угловая {body.AngularVelocity:F2}; поворот {rotation.Theta:F2}; " +
               $"тяга {shuttle.LastThrust.X:F2}, {shuttle.LastThrust.Y:F2}; " +
               $"цель {target.X:F1}, {target.Y:F1}; PID {compensation:F2}; " +
               $"пилоты {piloted}; руление {steering}; фаза {phase}";
    }

    private static string _xformToText(MapCoordinates coordinates)
    {
        return $"{coordinates.Position.X:F1}, {coordinates.Position.Y:F1}";
    }
}
