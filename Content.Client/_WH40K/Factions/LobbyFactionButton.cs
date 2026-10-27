using Content.Client._WH40K.CharacterCreation;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WH40K.Factions;

public sealed class LobbyFactionButton : Button
{
    private bool _primary;

    public bool Primary
    {
        get => _primary;
        set
        {
            _primary = value;
            DrawModeChanged();
        }
    }

    public LobbyFactionButton()
    {
        MinHeight = 32;
        ModulateSelfOverride = Color.White;
        Label.FontOverride = Wh40kOnboardingTypography.Text(IoCManager.Resolve<IResourceCache>(), 12);
        DrawModeChanged();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        var disabled = DrawMode == DrawModeEnum.Disabled;
        var hover = DrawMode == DrawModeEnum.Hover;
        var pressed = DrawMode == DrawModeEnum.Pressed;
        StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(disabled ? "#292929" : _primary
                ? pressed ? "#554427" : hover ? "#947744" : "#705B35"
                : pressed ? "#292929" : hover ? "#464646" : "#353535"),
            BorderColor = Color.FromHex(disabled ? "#424242" : _primary ? "#BFA36D" : "#656565"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 14,
            ContentMarginRightOverride = 14,
            ContentMarginTopOverride = 5,
            ContentMarginBottomOverride = 5,
        };
        // ContainerButton calls this virtual method before Button has created its label.
        if (Label != null)
            Label.FontColorOverride = Color.FromHex(disabled ? "#85828B" : _primary ? "#FFF0D0" : "#E0DDE5");
    }
}
