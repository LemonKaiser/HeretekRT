using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Maths;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.CharacterCreation;

public sealed class Wh40kSectionRule : Control
{
    private readonly Texture _ornament = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>("/Textures/_WH40K/Interface/section-rule.svg.192dpi.png").Texture;

    public Wh40kSectionRule()
    {
        SetHeight = 14;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var centre = (Vector2) PixelSize / 2;
        var width = MathF.Min(190 * UIScale, PixelWidth);
        var color = Color.FromHex("#93754570");
        handle.DrawLine(new Vector2(0, centre.Y), new Vector2(MathF.Max(0, centre.X - width / 2 - 8 * UIScale), centre.Y), color);
        handle.DrawLine(new Vector2(MathF.Min(PixelWidth, centre.X + width / 2 + 8 * UIScale), centre.Y), new Vector2(PixelWidth, centre.Y), color);
        handle.DrawTextureRect(_ornament, UIBox2.FromDimensions(centre - new Vector2(width / 2, 7 * UIScale), new Vector2(width, 14 * UIScale)));
    }
}
