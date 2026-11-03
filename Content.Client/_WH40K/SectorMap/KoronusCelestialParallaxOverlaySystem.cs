using System.Numerics;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.SectorMap;

/// <summary>
/// Registers the decorative orbital-space layer and maintains a shadowless client-side light at
/// each projected star position. It never creates server entities, physics bodies, grids or targets.
/// </summary>
public sealed class KoronusCelestialParallaxOverlaySystem : EntitySystem
{
    private const float CoronaRadiusMultiplier = 1.9f;

    [Dependency] private IEyeManager _eyes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _maps = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<string, EntityUid> _sunLights = new();
    private readonly HashSet<string> _activeStarIds = new();
    private readonly List<string> _removedStarIds = new();
    private MapId _sunLightMap = MapId.Nullspace;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new KoronusCelestialParallaxOverlay());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eyePosition = _eyes.CurrentEye.Position;
        if (eyePosition.MapId == MapId.Nullspace || !_maps.MapExists(eyePosition.MapId))
        {
            ResetSunLights();
            return;
        }

        var mapUid = _maps.GetMapEntityId(eyePosition.MapId);
        if (!TryComp<KoronusPlanetarySystemVisualComponent>(mapUid, out var visual) ||
            !_prototypes.TryIndex<KoronusSystemPrototype>(visual.SystemId, out var system) ||
            system.SpaceMode != KoronusSpaceMode.Planetary)
        {
            ResetSunLights();
            return;
        }

        if (_sunLightMap != eyePosition.MapId)
        {
            ResetSunLights();
            _sunLightMap = eyePosition.MapId;
        }

        _activeStarIds.Clear();
        var time = (float) _timing.CurTime.TotalSeconds;
        foreach (var star in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (star.System != system.ID || star.BodyType != KoronusCelestialBodyType.Star)
                continue;

            var positionAngle = visual.PositionAngleOverrides.TryGetValue(star.ID, out var angle)
                ? angle
                : star.OrbitPhase;
            var orbitalPosition = KoronusCelestialParallaxOverlay.GetOrbitalPosition(system, star, time, positionAngle);
            var slowness = KoronusCelestialParallaxOverlay.GetSceneryParallaxSlowness(star);
            var sceneryPosition = KoronusCelestialParallaxOverlay.GetSceneryPosition(
                orbitalPosition,
                eyePosition.Position,
                slowness);
            var lightRadius = KoronusCelestialParallaxOverlay.GetSceneryRadius(star) * CoronaRadiusMultiplier;

            if (lightRadius <= 0f)
                continue;

            _activeStarIds.Add(star.ID);
            var coordinates = new MapCoordinates(sceneryPosition, eyePosition.MapId);
            if (!_sunLights.TryGetValue(star.ID, out var lightUid) || TerminatingOrDeleted(lightUid))
            {
                lightUid = Spawn(null, coordinates);
                _sunLights[star.ID] = lightUid;
                var light = EnsureComp<PointLightComponent>(lightUid);
                _lights.SetCastShadows(lightUid, false, light);
                _lights.SetColor(lightUid, star.StarColor, light);
                _lights.SetEnergy(lightUid, 0.85f, light);
                _lights.SetFalloff(lightUid, 2.4f, light);
                _lights.SetCurveFactor(lightUid, 0.35f, light);
                _lights.SetRadius(lightUid, lightRadius, light);
                continue;
            }

            _transform.SetMapCoordinates(lightUid, coordinates);
            if (TryComp<PointLightComponent>(lightUid, out var existingLight))
            {
                _lights.SetColor(lightUid, star.StarColor, existingLight);
                _lights.SetRadius(lightUid, lightRadius, existingLight);
            }
        }

        _removedStarIds.Clear();
        foreach (var (id, uid) in _sunLights)
        {
            if (_activeStarIds.Contains(id))
                continue;

            if (!TerminatingOrDeleted(uid))
                QueueDel(uid);
            _removedStarIds.Add(id);
        }

        foreach (var id in _removedStarIds)
            _sunLights.Remove(id);
    }

    public override void Shutdown()
    {
        ResetSunLights();
        _overlays.RemoveOverlay<KoronusCelestialParallaxOverlay>();
        base.Shutdown();
    }

    private void ResetSunLights()
    {
        foreach (var light in _sunLights.Values)
        {
            if (!TerminatingOrDeleted(light))
                QueueDel(light);
        }

        _sunLights.Clear();
        _activeStarIds.Clear();
        _removedStarIds.Clear();
        _sunLightMap = MapId.Nullspace;
    }
}
