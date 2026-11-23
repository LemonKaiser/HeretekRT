using Content.Shared.CCVar;
using Content.Shared.Chat.TypingIndicator;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.Chat.TypingIndicator;

// Client-side typing system tracks user input in chat box
public sealed partial class TypingIndicatorSystem : SharedTypingIndicatorSystem
{
    [Dependency] private IGameTiming _time = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly TimeSpan _typingTimeout = TimeSpan.FromSeconds(2);
    private TimeSpan _lastTextChange;
    private bool _isClientTyping;
    private bool _isClientChatFocused;
    private bool ShowTyping => _cfg.GetCVar(CCVars.ChatShowTypingIndicator) && _cfg.GetCVar(CCVars.ChatShowMyTyping);

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.ChatShowTypingIndicator, OnShowTypingChanged);
        Subs.CVar(_cfg, CCVars.ChatShowMyTyping, OnShowTypingChanged);
    }

    public void ClientChangedChatText()
    {
        // don't update it if player don't want to show typing indicator
        if (!ShowTyping)
            return;

        // client typed something - show typing indicator
        _isClientTyping = true;
        ClientUpdateTyping();
        _lastTextChange = _time.CurTime;
    }

    public void ClientSubmittedChatText()
    {
        // client submitted text - hide typing indicator
        _isClientTyping = false;
        if (ShowTyping)
            ClientUpdateTyping();
    }

    public void ClientChangedChatFocus(bool isFocused)
    {
        _isClientChatFocused = isFocused;
        if (ShowTyping)
            ClientUpdateTyping();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_time.IsFirstTimePredicted)
            return;

        // check if client didn't changed chat text box for a long time
        if (_isClientTyping)
        {
            var dif = _time.CurTime - _lastTextChange;
            if (dif > _typingTimeout)
            {
                // client didn't typed anything for a long time - change indicator
                _isClientTyping = false;
                ClientUpdateTyping();
            }
        }
    }

    private void ClientUpdateTyping()
    {
        // check if player controls any pawn
        if (_playerManager.LocalEntity == null)
            return;
		
        var state = TypingIndicatorState.None;
        if (_isClientChatFocused && ShowTyping)
            state = _isClientTyping ? TypingIndicatorState.Typing : TypingIndicatorState.Idle;

        // send a networked event to server
        RaisePredictiveEvent(new TypingChangedEvent(state));
    }

    private void OnShowTypingChanged(bool showTyping)
    {
        if (!showTyping)
            _isClientTyping = false;

        ClientUpdateTyping();
    }
}
