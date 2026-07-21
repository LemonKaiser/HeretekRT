using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Repaints the portion of the active lobby image behind the drawer through a small
/// blur shader. This is the UI-safe equivalent of CSS backdrop-filter.
/// </summary>
public sealed class LobbyDrawerBlurBackdrop : Control
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private readonly ShaderInstance _blurShader;

    public Texture? BackgroundTexture { get; set; }
    public Vector2i BackgroundPixelSize { get; set; }
    public Vector2i BackgroundGlobalPixelPosition { get; set; }

    public LobbyDrawerBlurBackdrop()
    {
        IoCManager.InjectDependencies(this);
        _blurShader = _prototypeManager.Index<ShaderPrototype>("HeretekLobbyDrawerBlur").Instance();
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var texture = BackgroundTexture;
        if (texture == null || PixelSize.X <= 0 || PixelSize.Y <= 0
            || BackgroundPixelSize.X <= 0 || BackgroundPixelSize.Y <= 0)
        {
            return;
        }

        var textureSize = new Vector2(texture.Width, texture.Height);
        var backgroundSize = new Vector2(BackgroundPixelSize.X, BackgroundPixelSize.Y);
        var scale = MathF.Max(backgroundSize.X / textureSize.X, backgroundSize.Y / textureSize.Y);
        var drawnTextureSize = textureSize * scale;
        var drawnTextureOffset = (backgroundSize - drawnTextureSize) * 0.5f;
        var drawerOffset = new Vector2(
            GlobalPixelPosition.X - BackgroundGlobalPixelPosition.X,
            GlobalPixelPosition.Y - BackgroundGlobalPixelPosition.Y);
        var sourceTopLeft = (drawerOffset - drawnTextureOffset) / drawnTextureSize * textureSize;
        var sourceBottomRight = (drawerOffset + PixelSize - drawnTextureOffset) / drawnTextureSize * textureSize;

        handle.UseShader(_blurShader);
        handle.DrawTextureRectRegion(
            texture,
            UIBox2.FromDimensions(Vector2.Zero, PixelSize),
            new UIBox2(sourceTopLeft, sourceBottomRight));
        handle.UseShader(null);
    }
}
