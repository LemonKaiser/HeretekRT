using Content.Client._WH40K.UserInterface;

namespace Content.Client.Lobby.UI;

public sealed partial class LobbyGui
{
    internal bool IsAnimatedLobbyBackground => _isAnimatedLobbyBackground;

    private void ApplyOnboardingTheme()
    {
        Wh40kInterfaceTheme.ApplyControls(OnboardingShell);
        OnboardingShell.PanelOverride = Wh40kInterfaceTheme.Surface("#15120FED", "#75613D", 20, 20, 5);
        OnboardingTabsPanel.PanelOverride = Wh40kInterfaceTheme.Card(6);
        OnboardingNavigationPanel.PanelOverride = Wh40kInterfaceTheme.Card();
        OnboardingPreviewPanel.PanelOverride = Wh40kInterfaceTheme.PreviewPanel();
        OnboardingPreviewStage.PanelOverride = Wh40kInterfaceTheme.PreviewStage();
        if (OnboardingContentPanel.PanelOverride is OnboardingPageFrameStyleBox content)
        {
            content.BackgroundColor = Color.FromHex("#15120FED");
            content.BorderColor = Color.FromHex("#56452D");
        }

        Wh40kInterfaceTheme.UsePrimaryButton(OnboardingCompleteButton);
    }
}
