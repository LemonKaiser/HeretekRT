using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.CharacterCreation;

public sealed class Wh40kPortraitButton : Button
{
    private PanelContainer? _overlay;
    private TextureRect? _portrait;

    public Wh40kPortraitButton()
    {
        ToggleMode = true;
        MinWidth = 32;
        MinHeight = 36;
        Disabled = true;
        ToolTip = Loc.GetString("wh40k-personalization-portrait-toggle");
        OnToggled += args => SetPortraitVisible(args.Pressed);
    }

    public void Bind(PanelContainer overlay, TextureRect portrait)
    {
        _overlay = overlay;
        _portrait = portrait;
        overlay.PanelOverride = Wh40kPersonalizationTheme.Frame("#100E0B", "#69563B", 6);
        overlay.Visible = false;
    }

    public void SetPortrait(Texture? texture)
    {
        if (_portrait == null || _overlay == null)
            return;
        _portrait.Texture = texture;
        Disabled = texture == null;
        Pressed = false;
        _overlay.Visible = false;
    }

    internal void SetPortraitVisible(bool visible)
    {
        Pressed = visible && !Disabled && _portrait?.Texture != null;
        if (_overlay != null)
            _overlay.Visible = Pressed;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var centre = PixelSize / 2;
        var color = Color.FromHex("#E1C17E");
        var half = new Vector2(7, 9) * UIScale;
        handle.DrawRect(new UIBox2(centre - half, centre + half), color, false);
        handle.DrawCircle(centre - new Vector2(0, 3) * UIScale, 2.5f * UIScale, color, false);
        handle.DrawLine(centre + new Vector2(-4, 5) * UIScale, centre + new Vector2(-4, 3) * UIScale, color);
        handle.DrawLine(centre + new Vector2(-4, 3) * UIScale, centre + new Vector2(0, 1) * UIScale, color);
        handle.DrawLine(centre + new Vector2(0, 1) * UIScale, centre + new Vector2(4, 3) * UIScale, color);
        handle.DrawLine(centre + new Vector2(4, 3) * UIScale, centre + new Vector2(4, 5) * UIScale, color);
    }
}
