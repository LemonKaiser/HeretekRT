using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Content.Client._WH40K.CharacterCreation;
using Robust.Client.ResourceManagement;

namespace Content.Client._WH40K.UserInterface;

public sealed class Wh40kSigil : TextureRect
{
    public Wh40kSigil()
    {
        Texture = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>("/Textures/_WH40K/Interface/imperial-sigil.svg.192dpi.png").Texture;
        SetSize = new System.Numerics.Vector2(52);
        CanShrink = true; Stretch = StretchMode.KeepAspectCentered;
        HorizontalAlignment = HAlignment.Center; MouseFilter = MouseFilterMode.Ignore;
    }
}

public sealed class Wh40kStatusBadge : PanelContainer
{
    public Wh40kStatusBadge(string text, Color color)
    {
        PanelOverride = Wh40kInterfaceTheme.Surface("#100D0A66", color.ToHex(), 7, 2, 4);
        AddChild(new Label { Text = text, FontColorOverride = color,
            FontOverride = Wh40kOnboardingTypography.Text(IoCManager.Resolve<IResourceCache>(), 8) });
        VerticalAlignment = VAlignment.Center;
    }
}

public sealed class Wh40kNotice : PanelContainer
{
    public Wh40kNotice(string title, string text, bool warning = false)
    {
        AddStyleClass(warning ? "Wh40kNoticeWarning" : "Wh40kNoticeInfo");
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
        column.AddChild(new Label { Text = title });
        var body = new RichTextLabel(); body.SetMessage(text, Color.FromHex("#ADA391")); column.AddChild(body);
        AddChild(column);
    }
}
