using System.Numerics;
using Content.Client.Lobby.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Robust.Shared.Configuration;
using Content.Shared.CCVar;

namespace Content.Client._WH40K.UserInterface;

internal sealed class Wh40kWindowStyleBox : StyleBox
{
    private readonly IConfigurationManager _cfg = IoCManager.Resolve<IConfigurationManager>();
    private readonly LobbyRoundedStyleBox _base = MakeFrame("#17130F");
    private readonly bool _glass;
    private static readonly Color BackdropShade = Color.FromHex("#554C40");

    internal Wh40kWindowStyleBox(bool shadow = true, bool glass = true)
    {
        _glass = glass;
        if (!shadow) _base.ShadowColor = Color.Transparent;
    }

    private static LobbyRoundedStyleBox MakeFrame(string background) => new()
    {
        BackgroundColor = Color.FromHex(background),
        BorderColor = Color.FromHex("#8F744A9C"),
        BorderThickness = new Thickness(1),
        CornerRadius = 5,
        HighlightColor = Color.FromHex("#E4C37C18"),
        ShadowColor = Color.FromHex("#00000040"),
    };

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        _base.Draw(handle, box, uiScale);
        if (!_glass)
            return;
        var glass = IoCManager.Resolve<IUserInterfaceManager>().GetUIController<Wh40kInterfaceUIController>().Glass;
        if (glass.Texture is not { } texture || glass.ScreenSize.X <= 0 || glass.ScreenSize.Y <= 0)
            return;
        var inner = new Thickness(4 * uiScale).Deflate(box);
        if (inner.Width <= 0 || inner.Height <= 0) return;
        var origin = Vector2.Transform(inner.TopLeft, handle.GetTransform());
        var ratio = (Vector2) texture.Size / glass.ScreenSize;
        var region = new UIBox2(origin * ratio, (origin + inner.Size) * ratio);
        var transparency = Math.Clamp(_cfg.GetCVar(CCVars.WH40KUiGlassTransparency), 0, 1);
        // Blend straight onto the opaque base: the same tint without another full-panel fill or frame.
        handle.DrawTextureRectRegion(texture, inner, region, BackdropShade.WithAlpha(transparency));
    }
}
