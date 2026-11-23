using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.UserInterface;

/// <summary>The approved border-image corners, drawn above the header and contents.</summary>
internal sealed class Wh40kWindowTrim : Control
{
    private readonly Texture _texture;
    public Wh40kWindowTrim(IResourceCache resources)
    {
        _texture = resources.GetResource<TextureResource>("/Textures/_WH40K/Interface/frame-corners.svg.192dpi.png").Texture;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (Width < 100 || Height < 40)
            return;
        var inset = 3 * UIScale;
        var size = 20 * UIScale;
        for (var i = 0; i < 4; i++)
        {
            var right = i % 2 != 0;
            var bottom = i >= 2;
            var target = new Vector2(right ? PixelWidth - inset - size : inset, bottom ? PixelHeight - inset - size : inset);
            var source = new Vector2(right ? 176 : 0, bottom ? 176 : 0);
            handle.DrawTextureRectRegion(_texture, UIBox2.FromDimensions(target, new Vector2(size)),
                UIBox2.FromDimensions(source, new Vector2(80)), Color.White.WithAlpha(0.78f));
        }
    }
}
