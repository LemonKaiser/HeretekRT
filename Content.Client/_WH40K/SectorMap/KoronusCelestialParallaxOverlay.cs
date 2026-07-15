using System.Numerics;
using Content.Client.Parallax;
using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.SectorMap;

/// <summary>
/// Renders large, non-interactive celestial visuals behind the playable world. Positions use the
/// same authored orbit phase and speed as the shuttle NAV layer, but use parallax interpolation so
/// an orbital body reads as distant scenery rather than as a physical map object.
/// </summary>
public sealed class KoronusCelestialParallaxOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> FlatBodyShaderId = "KoronusCelestialTexture";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IMapManager _maps = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<ResPath, Texture> _textures = new();
    private readonly ShaderInstance _flatBodyShader;
    private readonly KoronusPlanetVisualRenderer _planetRenderer;
    private readonly KoronusSunVisualRenderer _sunRenderer;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public KoronusCelestialParallaxOverlay()
    {
        ZIndex = ParallaxSystem.ParallaxZIndex + 1;
        IoCManager.InjectDependencies(this);
        _flatBodyShader = _prototypes.Index<ShaderPrototype>(FlatBodyShaderId).InstanceUnique();
        _planetRenderer = new KoronusPlanetVisualRenderer(_prototypes, _resources);
        _sunRenderer = new KoronusSunVisualRenderer(_prototypes, _resources);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return false;

        var mapUid = _maps.GetMapEntityId(args.MapId);
        return _entities.HasComponent<KoronusPlanetarySystemVisualComponent>(mapUid);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mapUid = _maps.GetMapEntityId(args.MapId);
        if (!_entities.TryGetComponent<KoronusPlanetarySystemVisualComponent>(mapUid, out var visual) ||
            !_prototypes.TryIndex<KoronusSystemPrototype>(visual.SystemId, out var system))
        {
            return;
        }

        var eyePosition = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var time = (float) _timing.CurTime.TotalSeconds;
        foreach (var body in _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>())
        {
            if (body.System != system.ID)
                continue;

            var positionAngle = visual.PositionAngleOverrides.TryGetValue(body.ID, out var overrideAngle)
                ? overrideAngle
                : body.OrbitPhase;
            var orbitalPosition = GetOrbitalPosition(system, body, time, positionAngle);
            var slowness = Math.Clamp(body.BackgroundParallaxSlowness, 0f, 1f);
            var sceneryPosition = GetSceneryPosition(orbitalPosition, eyePosition, slowness);
            var sceneryRadius = GetSceneryRadius(body);
            if (sceneryRadius <= 0f)
                continue;

            if (body.BodyType == KoronusCelestialBodyType.Star)
            {
                _sunRenderer.Draw(args.WorldHandle, sceneryPosition, sceneryRadius);
                continue;
            }

            DrawBody(
                args.WorldHandle,
                sceneryPosition,
                sceneryRadius,
                body,
                system.NavigationCenter - orbitalPosition);
        }
    }

    private void DrawBody(
        DrawingHandleWorld handle,
        Vector2 centre,
        float radius,
        KoronusCelestialBodyPrototype body,
        Vector2 directionToStar)
    {
        if ((body.BodyType is KoronusCelestialBodyType.Planet or
             KoronusCelestialBodyType.Moon or
             KoronusCelestialBodyType.Asteroid) &&
            _planetRenderer.Draw(handle, centre, radius, body, directionToStar))
        {
            return;
        }

        if (body.Texture is not { } texturePath)
            return;

        var texture = GetTexture(texturePath);
        var size = new Vector2(radius * 2f);
        handle.UseShader(_flatBodyShader);
        handle.DrawTextureRect(texture, Box2.FromDimensions(centre - size / 2f, size));
        handle.UseShader(null);
    }

    internal static Vector2 GetOrbitalPosition(
        KoronusSystemPrototype system,
        KoronusCelestialBodyPrototype body,
        float time,
        float? positionAngle = null)
    {
        if (body.OrbitRadius <= 0f)
            return system.NavigationCenter;

        var phase = ((positionAngle ?? body.OrbitPhase) + body.OrbitAngularSpeed * time) * MathF.PI / 180f;
        return system.NavigationCenter + new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * body.OrbitRadius;
    }

    internal static Vector2 GetSceneryPosition(Vector2 orbitalPosition, Vector2 eyePosition, float parallaxSlowness)
    {
        // This follows the same convention as ParallaxOverlay: 0 is world-anchored, 1 is locked to
        // the screen. Keeping authored values below 1 preserves both the orbital direction and an
        // observable amount of movement when the player changes position.
        return Vector2.Lerp(orbitalPosition, eyePosition, Math.Clamp(parallaxSlowness, 0f, 1f));
    }

    internal static float GetSceneryRadius(KoronusCelestialBodyPrototype body)
    {
        var authoredRadius = body.BackgroundVisualRadius > 0f
            ? body.BackgroundVisualRadius
            : body.NavVisualRadius;
        return authoredRadius * (1f - Math.Clamp(body.BackgroundParallaxSlowness, 0f, 1f));
    }

    private Texture GetTexture(ResPath path)
    {
        if (_textures.TryGetValue(path, out var texture))
            return texture;

        texture = _resources.GetResource<TextureResource>(path).Texture;
        _textures[path] = texture;
        return texture;
    }
}
