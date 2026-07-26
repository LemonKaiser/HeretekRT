using Content.Client.Eui;
using Content.Shared.Eui;
using Content.Shared._WH40K.Progression;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._WH40K.Progression;

[UsedImplicitly]
public sealed class Wh40kPartyInvitationEui : BaseEui
{
    private readonly Wh40kPartyInvitationWindow _window;
    private bool _responded;

    public Wh40kPartyInvitationEui()
    {
        _window = new Wh40kPartyInvitationWindow();
        _window.AcceptButton.OnPressed += _ => Respond(Wh40kPartyInvitationChoice.Accept);
        _window.DeclineButton.OnPressed += _ => Respond(Wh40kPartyInvitationChoice.Decline);
        _window.OnClose += () =>
        {
            if (!_responded)
                Respond(Wh40kPartyInvitationChoice.Decline);
        };
    }

    public override void Opened()
    {
        IoCManager.Resolve<IClyde>().RequestWindowAttention();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _responded = true;
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is Wh40kPartyInvitationEuiState invitation)
            _window.UpdateState(invitation);
    }

    private void Respond(Wh40kPartyInvitationChoice choice)
    {
        if (_responded)
            return;

        _responded = true;
        SendMessage(new Wh40kPartyInvitationChoiceMessage(choice));
        _window.Close();
    }
}
