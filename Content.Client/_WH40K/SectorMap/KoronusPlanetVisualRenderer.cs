using System.Numerics;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.SectorMap;

/// <summary>
/// Shared client-only renderer for the same textured planet profile in world parallax and NAV UI.
/// It owns no entities and references no server assembly types.
/// </summary>
public sealed class KoronusPlanetVisualRenderer
{
    private const float SurfaceDiameterScale = 2.04f;
    private const float AtmosphereDiameterScale = 2.5f;

    private static readonly ProtoId<ShaderPrototype> SurfaceShaderId = "KoronusPlanetSurface";
    private static readonly ProtoId<ShaderPrototype> AtmosphereShaderId = "KoronusPlanetAtmosphere";
    private static readonly ResPath CloudNoiseTexturePath = new("/Textures/Parallaxes/noise.png");

    private readonly IPrototypeManager _prototypes;
    private readonly IResourceCache _resources;
    private readonly Dictionary<ResPath, Texture> _textures = new();
    private readonly Dictionary<string, ShaderInstance> _surfaceShaders = new();
    private readonly Dictionary<string, ShaderInstance> _atmosphereShaders = new();

    public KoronusPlanetVisualRenderer(IPrototypeManager prototypes, IResourceCache resources)
    {
        _prototypes = prototypes;
        _resources = resources;
    }

    public bool Draw(
        DrawingHandleWorld handle,
        Vector2 centre,
        float radius,
        KoronusCelestialBodyPrototype body,
        Vector2 directionToStar)
    {
        if (!TryPrepare(body, directionToStar, out var surface, out var atmosphere))
            return false;

        var previousShader = handle.GetShader();
        if (body.AtmosphereIntensity > 0f)
        {
            var atmosphereSize = new Vector2(radius * AtmosphereDiameterScale);
            handle.UseShader(atmosphere);
            handle.DrawRect(Box2.FromDimensions(centre - atmosphereSize / 2f, atmosphereSize), Color.White);
        }

        var surfaceSize = new Vector2(radius * SurfaceDiameterScale);
        handle.UseShader(surface);
        handle.DrawRect(Box2.FromDimensions(centre - surfaceSize / 2f, surfaceSize), Color.White);
        handle.UseShader(previousShader);
        return true;
    }

    public bool Draw(
        DrawingHandleScreen handle,
        Vector2 centre,
        float radius,
        KoronusCelestialBodyPrototype body,
        Vector2 directionToStar)
    {
        if (!TryPrepare(body, directionToStar, out var surface, out var atmosphere))
            return false;

        var previousShader = handle.GetShader();
        if (body.AtmosphereIntensity > 0f)
        {
            var atmosphereSize = new Vector2(radius * AtmosphereDiameterScale);
            handle.UseShader(atmosphere);
            handle.DrawRect(UIBox2.FromDimensions(centre - atmosphereSize / 2f, atmosphereSize), Color.White);
        }

        var surfaceSize = new Vector2(radius * SurfaceDiameterScale);
        handle.UseShader(surface);
        handle.DrawRect(UIBox2.FromDimensions(centre - surfaceSize / 2f, surfaceSize), Color.White);
        handle.UseShader(previousShader);
        return true;
    }

    private bool TryPrepare(
        KoronusCelestialBodyPrototype body,
        Vector2 directionToStar,
        out ShaderInstance surface,
        out ShaderInstance atmosphere)
    {
        surface = default!;
        atmosphere = default!;
        if (body.Texture is not { } texturePath)
            return false;

        if (!_surfaceShaders.TryGetValue(body.ID, out var cachedSurface))
        {
            cachedSurface = _prototypes.Index<ShaderPrototype>(SurfaceShaderId).InstanceUnique();
            _surfaceShaders[body.ID] = cachedSurface;
        }
        surface = cachedSurface;

        if (!_atmosphereShaders.TryGetValue(body.ID, out var cachedAtmosphere))
        {
            cachedAtmosphere = _prototypes.Index<ShaderPrototype>(AtmosphereShaderId).InstanceUnique();
            _atmosphereShaders[body.ID] = cachedAtmosphere;
        }
        atmosphere = cachedAtmosphere;

        var texture = GetTexture(texturePath);
        var cloudNoiseTexture = GetTexture(CloudNoiseTexturePath);
        var lightDirection = GetLightDirection(directionToStar);
        surface.SetParameter("SurfaceTexture", texture);
        surface.SetParameter("CloudNoiseTexture", cloudNoiseTexture);
        surface.SetParameter("SurfaceRotationSpeed", body.SurfaceRotationSpeed);
        surface.SetParameter("SurfaceRotationPhase", body.SurfaceRotationPhase);
        surface.SetParameter("AxialTilt", body.AxialTilt);
        surface.SetParameter("CloudRotationSpeed", body.CloudRotationSpeed);
        surface.SetParameter("CloudOpacity", Math.Clamp(body.CloudOpacity, 0f, 1f));
        surface.SetParameter("CloudScale", Math.Max(body.CloudScale, 0.1f));
        surface.SetParameter("CloudSeed", body.CloudSeed);
        surface.SetParameter("CloudColor", body.CloudColor);
        var atmosphereIntensity = Math.Clamp(body.AtmosphereIntensity, 0f, 2f);
        surface.SetParameter(
            "AtmosphereColor",
            body.AtmosphereColor.WithAlpha(body.AtmosphereColor.A * atmosphereIntensity));
        surface.SetParameter("LightDirection", lightDirection);

        atmosphere.SetParameter("AtmosphereColor", body.AtmosphereColor);
        atmosphere.SetParameter("AtmosphereIntensity", atmosphereIntensity);
        atmosphere.SetParameter("LightDirection", lightDirection);
        return true;
    }

    private Texture GetTexture(ResPath path)
    {
        if (_textures.TryGetValue(path, out var texture))
            return texture;

        texture = _resources.GetResource<TextureResource>(path).Texture;
        _textures[path] = texture;
        return texture;
    }

    private static Vector3 GetLightDirection(Vector2 direction)
    {
        if (direction.LengthSquared() < 0.0001f)
            return Vector3.Normalize(new Vector3(-0.65f, 0.35f, 0.72f));

        direction = Vector2.Normalize(direction);
        // UI/world Y points down in shader UVs, while the reconstructed sphere normal uses Y-up.
        return Vector3.Normalize(new Vector3(direction.X, -direction.Y, 0.72f));
    }
}
