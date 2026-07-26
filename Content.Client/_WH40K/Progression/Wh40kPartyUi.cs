using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared._WH40K.Progression;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Progression;

public sealed partial class Wh40kPartyUi : UIFragment
{
    private Wh40kPartyUiFragment? _fragment;
    private BoundUserInterface? _userInterface;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _userInterface = userInterface;
        _fragment = new Wh40kPartyUiFragment();
        _fragment.OnInvite += ckey => Send(new Wh40kPartyUiMessage(Wh40kPartyUiAction.Invite, ckey));
        _fragment.OnLeave += () => Send(new Wh40kPartyUiMessage(Wh40kPartyUiAction.Leave));
        _fragment.OnKick += userId => Send(new Wh40kPartyUiMessage(
            Wh40kPartyUiAction.Kick,
            targetUserId: userId));
        _fragment.OnInvitesAllowedChanged += allowed => Send(new Wh40kPartyUiMessage(
            Wh40kPartyUiAction.SetInvitesAllowed,
            allowInvites: allowed));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }

    public override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is not Wh40kPartySnapshotBuiMessage snapshotMessage)
            return;

        _fragment?.UpdateSnapshot(snapshotMessage.Snapshot, snapshotMessage.Status);
    }

    private void Send(Wh40kPartyUiMessage message)
    {
        if (_userInterface == null || _fragment == null)
            return;

        _fragment.SetPending(true);
        _userInterface.SendMessage(new CartridgeUiMessage(message));
    }
}
