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
            if (value)
                AddStyleClass(UserInterface.Wh40kInterfaceTheme.PrimaryButton);
            else
                RemoveStyleClass(UserInterface.Wh40kInterfaceTheme.PrimaryButton);
        }
    }

    public LobbyFactionButton()
    {
        MinHeight = 32;
        ModulateSelfOverride = Color.White;
        Label.FontOverride = Wh40kOnboardingTypography.Text(IoCManager.Resolve<IResourceCache>(), 12);
    }
}
