using System.Numerics;
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
    private static readonly ResPath NoiseTexturePath = new("/Textures/Parallaxes/noise.png");

    private readonly ShaderInstance _surfaceShader;
    private readonly ShaderInstance _coronaShader;

    public KoronusSunVisualRenderer(IPrototypeManager prototypes, IResourceCache resources)
    {
        _surfaceShader = prototypes.Index<ShaderPrototype>(SurfaceShaderId).InstanceUnique();
        _coronaShader = prototypes.Index<ShaderPrototype>(CoronaShaderId).InstanceUnique();
        _surfaceShader.SetParameter(
            "NoiseTexture",
            resources.GetResource<TextureResource>(NoiseTexturePath).Texture);
    }

    public void Draw(DrawingHandleWorld handle, Vector2 centre, float radius)
    {
        var previousShader = handle.GetShader();
        var coronaSize = new Vector2(radius * CoronaDiameterScale);
        handle.UseShader(_coronaShader);
        handle.DrawRect(Box2.FromDimensions(centre - coronaSize / 2f, coronaSize), Color.White);

        var surfaceSize = new Vector2(radius * SurfaceDiameterScale);
        handle.UseShader(_surfaceShader);
        handle.DrawRect(Box2.FromDimensions(centre - surfaceSize / 2f, surfaceSize), Color.White);
        handle.UseShader(previousShader);
    }

    public void Draw(DrawingHandleScreen handle, Vector2 centre, float radius)
    {
        var previousShader = handle.GetShader();
        var coronaSize = new Vector2(radius * CoronaDiameterScale);
        handle.UseShader(_coronaShader);
        handle.DrawRect(UIBox2.FromDimensions(centre - coronaSize / 2f, coronaSize), Color.White);

        var surfaceSize = new Vector2(radius * SurfaceDiameterScale);
        handle.UseShader(_surfaceShader);
        handle.DrawRect(UIBox2.FromDimensions(centre - surfaceSize / 2f, surfaceSize), Color.White);
        handle.UseShader(previousShader);
    }
}
