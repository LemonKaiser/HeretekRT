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
/// Registers the decorative orbital-space layer and maintains one shadowless client-side light at
/// the projected star position. It never creates server entities, physics bodies, grids or targets.
/// </summary>
public sealed class KoronusCelestialParallaxOverlaySystem : EntitySystem
{
    private const float CoronaRadiusMultiplier = 1.9f;

    private static readonly Color SunlightColor = Color.FromHex("#FFAA52");

    [Dependency] private IEyeManager _eyes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _maps = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityUid? _sunLight;

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
            ResetSunLight();
            return;
        }

        var mapUid = _maps.GetMapEntityId(eyePosition.MapId);
        if (!TryComp<KoronusPlanetarySystemVisualComponent>(mapUid, out var visual) ||
            !_prototypes.TryIndex<KoronusSystemPrototype>(visual.SystemId, out var system) ||
            system.SpaceMode != KoronusSpaceMode.Planetary)
        {
            ResetSunLight();
            return;
        }

        KoronusCelestialBodyPrototype? star = null;
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.System == system.ID && body.BodyType == KoronusCelestialBodyType.Star)
            {
                star = body;
                break;
            }
        }

        if (star == null)
        {
            ResetSunLight();
            return;
        }

        var time = (float) _timing.CurTime.TotalSeconds;
        var orbitalPosition = KoronusCelestialParallaxOverlay.GetOrbitalPosition(system, star, time);
        var slowness = Math.Clamp(star.BackgroundParallaxSlowness, 0f, 1f);
        var sceneryPosition = KoronusCelestialParallaxOverlay.GetSceneryPosition(
            orbitalPosition,
            eyePosition.Position,
            slowness);
        var lightRadius = KoronusCelestialParallaxOverlay.GetSceneryRadius(star) * CoronaRadiusMultiplier;

        if (lightRadius <= 0f)
        {
            ResetSunLight();
            return;
        }

        var coordinates = new MapCoordinates(sceneryPosition, eyePosition.MapId);
        if (_sunLight == null || Deleted(_sunLight.Value))
        {
            _sunLight = Spawn(null, coordinates);
            var light = EnsureComp<PointLightComponent>(_sunLight.Value);
            _lights.SetCastShadows(_sunLight.Value, false, light);
            _lights.SetColor(_sunLight.Value, SunlightColor, light);
            _lights.SetEnergy(_sunLight.Value, 0.85f, light);
            _lights.SetFalloff(_sunLight.Value, 2.4f, light);
            _lights.SetCurveFactor(_sunLight.Value, 0.35f, light);
            _lights.SetRadius(_sunLight.Value, lightRadius, light);
            return;
        }

        _transform.SetMapCoordinates(_sunLight.Value, coordinates);
        if (TryComp<PointLightComponent>(_sunLight.Value, out var existingLight))
            _lights.SetRadius(_sunLight.Value, lightRadius, existingLight);
    }

    public override void Shutdown()
    {
        ResetSunLight();
        _overlays.RemoveOverlay<KoronusCelestialParallaxOverlay>();
        base.Shutdown();
    }

    private void ResetSunLight()
    {
        if (_sunLight == null)
            return;

        if (!Deleted(_sunLight.Value))
            QueueDel(_sunLight.Value);

        _sunLight = null;
    }
}
