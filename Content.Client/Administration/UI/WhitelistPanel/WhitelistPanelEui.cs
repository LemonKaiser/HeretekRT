using Content.Client.Eui;
using Content.Shared.Administration.Whitelist;
using Content.Shared.Eui;

namespace Content.Client.Administration.UI.WhitelistPanel;

public sealed class WhitelistPanelEui : BaseEui
{
    private WhitelistPanelWindow? _window;
    private bool _closing;

    public override void Opened()
    {
        _closing = false;
        _window = new WhitelistPanelWindow();
        _window.OnCloseRequested += () =>
        {
            if (!_closing)
                SendMessage(new CloseEuiMessage());
        };
        _window.OnRefreshRequested += () => SendMessage(new WhitelistPanelRefreshMessage());
        _window.OnPlayerAddRequested += ckey => SendMessage(new WhitelistPanelAddPlayerMessage(ckey));
        _window.OnPlayerRemoveRequested += userId => SendMessage(new WhitelistPanelRemovePlayerMessage(userId));
        _window.OnWhitelistEnabledChanged += enabled => SendMessage(new WhitelistPanelSetEnabledMessage(enabled));
        _window.OnKickNonWhitelistedRequested += () => SendMessage(new WhitelistPanelKickNonWhitelistedMessage());
        _window.OpenCentered();
    }

    public override void Closed()
    {
        if (_window == null)
            return;

        _closing = true;
        _window.Close();
        _window.Orphan();
        _window = null;
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WhitelistPanelEuiState whitelistState)
            _window?.UpdateState(whitelistState);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        if (msg is WhitelistPanelNoticeMessage notice)
            _window?.ShowNotice(notice);
    }
}
