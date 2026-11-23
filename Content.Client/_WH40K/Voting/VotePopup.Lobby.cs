using Content.Client._WH40K.CharacterCreation;
using Content.Client.Lobby.UI;
using Content.Client._WH40K.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.Voting.UI;

public sealed partial class VotePopup
{
    private bool _lobbyStyle;

    protected override void EnteredTree()
    {
        base.EnteredTree();
        UserInterfaceManager.GetUIController<Wh40kInterfaceUIController>().RegisterFloatingPanel(this);
        Wh40kInterfaceTheme.ApplyControls(this);
        for (var ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is not LobbyGui)
                continue;

            ApplyLobbyStyle();
            break;
        }
    }

    protected override void ExitedTree()
    {
        UserInterfaceManager.GetUIController<Wh40kInterfaceUIController>().UnregisterFloatingPanel(this);
        base.ExitedTree();
    }

    private void ApplyLobbyStyle()
    {
        if (_lobbyStyle)
            return;

        _lobbyStyle = true;
        var resources = IoCManager.Resolve<IResourceCache>();
        var paper = Color.FromHex("#DFD7C3");
        var gold = Color.FromHex("#B69754");
        var technical = Wh40kOnboardingTypography.Technical(resources, 10);

        MinWidth = 0;
        MaxWidth = float.PositiveInfinity;
        HorizontalExpand = true;
        Modulate = Color.White;
        VoteContent.Margin = new Thickness(10);
        VoteContent.SeparationOverride = 7;
        LobbyHeading.Visible = true;
        LobbyHeading.Text = Loc.GetString("wh40k-lobby-vote-heading");
        LobbyHeading.FontOverride = technical;
        LobbyHeading.FontColorOverride = gold;
        VoteCaller.FontOverride = technical;
        VoteCaller.FontColorOverride = Color.FromHex("#9C9788");
        VoteCaller.ClipText = true;
        VoteTitle.Stylesheet = new Stylesheet(new[]
        {
            new StyleRule(new SelectorElement(typeof(RichTextLabel), null, null, null), new[]
            {
                new StyleProperty("font", Wh40kOnboardingTypography.Display(resources, 16)),
                new StyleProperty("font-color", paper),
            }),
        });
        VoteOptionsContainer.Columns = _voteButtons.Length > 1 ? 2 : 1;
        VoteOptionsContainer.HorizontalExpand = true;
        VoteTimer.SeparationOverride = 10;
        VoteTimer.Margin = new Thickness(0f, 4f, 0f, 0f);
        TimeLeftBar.Margin = new Thickness(0f);
        TimeLeftBar.MinHeight = 4;
        TimeLeftBar.SetHeight = 4;
        TimeLeftBar.VerticalAlignment = VAlignment.Center;
        TimeLeftBar.BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#373325") };
        TimeLeftBar.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = gold };
        TimeLeftText.FontOverride = Wh40kOnboardingTypography.Technical(resources, 12);
        TimeLeftText.FontColorOverride = gold;

        foreach (var button in _voteButtons)
        {
            button.ModulateSelfOverride = Color.White;
            button.HorizontalExpand = true;
            button.MinWidth = 0;
            button.MinHeight = 36;
            button.Margin = new Thickness(0f, 0f, 6f, 4f);
            button.Label.ClipText = true;
        }

    }
}
