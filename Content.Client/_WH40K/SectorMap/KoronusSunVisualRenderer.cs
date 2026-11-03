using System.Numerics;
using System.Linq;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.SectorMap;

/// <summary>
/// Draws one consistent procedural star in both the world parallax and the shuttle NAV display.
/// The renderer is client-only and restores the drawing handle's previous shader after each pass.
/// </summary>
public sealed class KoronusSunVisualRenderer
{
    private const float CoronaDiameterScale = 3.8f;
    private const float SurfaceDiameterScale = 2.08f;

    private static readonly ProtoId<ShaderPrototype> SurfaceShaderId = "KoronusProceduralSun";
    private static readonly ProtoId<ShaderPrototype> CoronaShaderId = "KoronusProceduralSunCorona";
    private static readonly ProtoId<ShaderPrototype> AccretingSurfaceShaderId = "KoronusAccretingSun";
    private static readonly ProtoId<ShaderPrototype> AccretingCoronaShaderId = "KoronusAccretingSunCorona";
    private static readonly ProtoId<ShaderPrototype> AccretionShaderId = "KoronusTwinsTidalRibbon";
    private static readonly ResPath NoiseTexturePath = new("/Textures/Parallaxes/noise.png");

    private readonly ShaderInstance _surfaceShader;
    private readonly ShaderInstance _coronaShader;
    private readonly ShaderInstance _accretingSurfaceShader;
    private readonly ShaderInstance _accretingCoronaShader;
    private readonly ShaderInstance _accretionShader;
    private readonly IPrototypeManager _prototypes;
    private readonly Dictionary<(Color Color, KoronusStarVisualStyle Style),
        (ShaderInstance Surface, ShaderInstance Corona)> _starShaders = new();

    public KoronusSunVisualRenderer(IPrototypeManager prototypes, IResourceCache resources)
    {
        _prototypes = prototypes;
        _surfaceShader = prototypes.Index<ShaderPrototype>(SurfaceShaderId).InstanceUnique();
        _coronaShader = prototypes.Index<ShaderPrototype>(CoronaShaderId).InstanceUnique();
        _accretingSurfaceShader = prototypes.Index<ShaderPrototype>(AccretingSurfaceShaderId).InstanceUnique();
        _accretingCoronaShader = prototypes.Index<ShaderPrototype>(AccretingCoronaShaderId).InstanceUnique();
        _accretionShader = prototypes.Index<ShaderPrototype>(AccretionShaderId).InstanceUnique();
        _accretingSurfaceShader.SetParameter(
            "NoiseTexture",
            resources.GetResource<TextureResource>(NoiseTexturePath).Texture);
    }

    public bool IsAccretionCompanion(KoronusCelestialBodyPrototype body)
    {
        return body.BodyType == KoronusCelestialBodyType.Star &&
               _prototypes.EnumeratePrototypes<KoronusCelestialBodyPrototype>().Any(donor =>
                   donor.ID != body.ID && donor.BodyType == KoronusCelestialBodyType.Star &&
                   donor.System == body.System && donor.NavVisualRadius > 0f &&
                   donor.AccretionTarget == body.ID);
    }

    public void Draw(DrawingHandleWorld handle, Vector2 centre, float radius, Color starColor,
        KoronusStarVisualStyle visualStyle = KoronusStarVisualStyle.Magnetic)
    {
        var (surface, corona) = ConfigureStar(starColor, visualStyle);
        var previousShader = handle.GetShader();
        var coronaSize = new Vector2(radius * CoronaDiameterScale);
        handle.UseShader(corona);
        handle.DrawRect(Box2.FromDimensions(centre - coronaSize / 2f, coronaSize), Color.White);

        var surfaceSize = new Vector2(radius * SurfaceDiameterScale);
        handle.UseShader(surface);
        handle.DrawRect(Box2.FromDimensions(centre - surfaceSize / 2f, surfaceSize), Color.White);
        handle.UseShader(previousShader);
    }

    public void Draw(DrawingHandleScreen handle, Vector2 centre, float radius, Color starColor,
        KoronusStarVisualStyle visualStyle = KoronusStarVisualStyle.Magnetic)
    {
        var (surface, corona) = ConfigureStar(starColor, visualStyle);
        var previousShader = handle.GetShader();
        var coronaSize = new Vector2(radius * CoronaDiameterScale);
        handle.UseShader(corona);
        handle.DrawRect(UIBox2.FromDimensions(centre - coronaSize / 2f, coronaSize), Color.White);

        var surfaceSize = new Vector2(radius * SurfaceDiameterScale);
        handle.UseShader(surface);
        handle.DrawRect(UIBox2.FromDimensions(centre - surfaceSize / 2f, surfaceSize), Color.White);
        handle.UseShader(previousShader);
    }

    private (ShaderInstance Surface, ShaderInstance Corona) ConfigureStar(Color starColor,
        KoronusStarVisualStyle visualStyle)
    {
        var key = (Color: starColor.WithAlpha(1f), Style: visualStyle);
        if (_starShaders.TryGetValue(key, out var cached))
            return cached;

        // Draw commands retain a shader handle until the frame is submitted. Changing its colour
        // for the next star would also recolour every earlier queued star using that instance.
        var surface = (visualStyle == KoronusStarVisualStyle.Accreting ? _accretingSurfaceShader : _surfaceShader).Duplicate();
        var corona = (visualStyle == KoronusStarVisualStyle.Accreting ? _accretingCoronaShader : _coronaShader).Duplicate();
        surface.SetParameter("StarColor", key.Color);
        corona.SetParameter("StarColor", key.Color);
        surface.MakeImmutable();
        corona.MakeImmutable();
        _starShaders.Add(key, (surface, corona));
        return (surface, corona);
    }

    public void DrawAccretion(DrawingHandleWorld handle, Vector2 donor, float donorRadius, Color donorColor,
        Vector2 target, float targetRadius, Color targetColor)
    {
        if (!ConfigureAccretion(donor, donorRadius, donorColor, target, targetRadius, targetColor,
                false, out var lower, out var size))
            return;

        var previousShader = handle.GetShader();
        handle.UseShader(_accretionShader);
        handle.DrawRect(Box2.FromDimensions(lower, size), Color.White);
        handle.UseShader(previousShader);
    }

    public void DrawAccretion(DrawingHandleScreen handle, Vector2 donor, float donorRadius, Color donorColor,
        Vector2 target, float targetRadius, Color targetColor)
    {
        if (!ConfigureAccretion(donor, donorRadius, donorColor, target, targetRadius, targetColor,
                true, out var lower, out var size))
            return;

        var previousShader = handle.GetShader();
        handle.UseShader(_accretionShader);
        handle.DrawRect(UIBox2.FromDimensions(lower, size), Color.White);
        handle.UseShader(previousShader);
    }

    private bool ConfigureAccretion(Vector2 donor, float donorRadius, Color donorColor,
        Vector2 target, float targetRadius, Color targetColor, bool flipY,
        out Vector2 lower, out Vector2 size)
    {
        lower = size = Vector2.Zero;
        var distance = Vector2.Distance(donor, target);
        if (distance <= 0.001f || donorRadius <= 0f || targetRadius <= 0f)
            return false;

        var padding = new Vector2(MathF.Max(donorRadius * 3.1f, targetRadius * 3.3f));
        lower = Vector2.Min(donor, target) - padding;
        size = Vector2.Max(donor, target) + padding - lower;
        _accretionShader.SetParameter("QuadSize", size / distance);
        _accretionShader.SetParameter("DonorPosition", (donor - lower) / distance);
        _accretionShader.SetParameter("TargetPosition", (target - lower) / distance);
        _accretionShader.SetParameter("DonorRadius", donorRadius / distance);
        _accretionShader.SetParameter("TargetRadius", targetRadius / distance);
        _accretionShader.SetParameter("DonorColor", donorColor.WithAlpha(1f));
        _accretionShader.SetParameter("TargetColor", targetColor.WithAlpha(1f));
        _accretionShader.SetParameter("FlipY", flipY ? 1f : 0f);
        return true;
    }
}
