using Content.Client._WH40K.CharacterCreation;
using Content.Client.Lobby.UI;
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
        for (var ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is not LobbyGui)
                continue;

            ApplyLobbyStyle();
            break;
        }
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
        VoteFrame.RemoveStyleClass("AngleRect");
        VoteFrame.PanelOverride = new OnboardingPageFrameStyleBox
        {
            BackgroundColor = Color.FromHex("#10110FEF"),
            BorderColor = Color.FromHex("#B6975480"),
        };
        VoteContent.Margin = new Thickness(20f, 16f, 20f, 14f);
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
                new StyleProperty("font", Wh40kOnboardingTypography.Display(resources, 21)),
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
            button.Label.FontOverride = Wh40kOnboardingTypography.Display(resources, _voteButtons.Length > 1 ? 14 : 16);
            button.Label.ClipText = true;
            button.Label.FontColorOverride = paper;
            button.OnMouseEntered += _ => UpdateLobbyButton(button, true);
            button.OnMouseExited += _ => UpdateLobbyButton(button, false);
            button.OnToggled += _ => UpdateLobbyButtons();
        }

        UpdateLobbyButtons();
    }

    private void UpdateLobbyButtons()
    {
        foreach (var button in _voteButtons)
            UpdateLobbyButton(button, button.IsHovered);
    }

    private static void UpdateLobbyButton(Button button, bool hovered)
    {
        var active = button.Pressed || hovered;
        button.Label.FontColorOverride = Color.FromHex(button.Pressed ? "#FFF0B8" : active ? "#E5C879" : "#DFD7C3");
        button.StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(button.Pressed ? "#826321EA" : active ? "#40361FDA" : "#24241BC0"),
            BorderColor = Color.FromHex(active ? "#D8B966" : "#8C77494D"),
            BorderThickness = new Thickness(button.Pressed ? 3f : 1f, 0f, 0f, 0f),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 6,
            ContentMarginBottomOverride = 6,
        };
        button.ToolTip = button.Text;
    }
}
