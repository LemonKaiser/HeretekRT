using Content.Server.EUI;
using Content.Shared.Eui;
using Content.Shared._WH40K.Progression;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Progression;

public sealed class Wh40kPartyInvitationEui : BaseEui
{
    private readonly Guid _invitationId;
    private readonly NetUserId _leaderUserId;
    private readonly Wh40kRpgPdaSystem _system;
    private readonly Wh40kPartyInvitationEuiState _state;
    private readonly DateTime _expiresAt;
    private bool _handled;

    public Wh40kPartyInvitationEui(
        Wh40kPartyInvitation invitation,
        string leaderCkey,
        Wh40kRpgPdaSystem system)
    {
        _invitationId = invitation.Id;
        _leaderUserId = invitation.LeaderUserId;
        _system = system;
        _expiresAt = invitation.ExpiresAt;
        _state = new Wh40kPartyInvitationEuiState(
            leaderCkey,
            _expiresAt.ToUniversalTime().Ticks);
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
        var remaining = _expiresAt - DateTime.UtcNow;
        Timer.Spawn(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, Expire);
    }

    public override EuiStateBase GetNewState()
    {
        return _state;
    }

    public override async void HandleMessage(EuiMessageBase msg)
    {
        if (_handled || msg is not Wh40kPartyInvitationChoiceMessage choice)
        {
            base.HandleMessage(msg);
            return;
        }

        Resolve(choice.Choice == Wh40kPartyInvitationChoice.Accept);
    }

    private void Expire()
    {
        Resolve(false);
    }

    private async void Resolve(bool accept)
    {
        if (_handled || IsShutDown)
            return;

        _handled = true;
        try
        {
            await _system.ResolveInvitationAsync(
                Player,
                _leaderUserId,
                _invitationId,
                accept);
        }
        finally
        {
            if (!IsShutDown)
                Close();
        }
    }
}
