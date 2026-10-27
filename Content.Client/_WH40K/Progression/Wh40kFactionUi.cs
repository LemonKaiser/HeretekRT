using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared._WH40K.Progression;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Progression;

public sealed partial class Wh40kFactionUi : UIFragment
{
    private Wh40kFactionUiFragment? _fragment;
    private BoundUserInterface? _userInterface;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _userInterface = userInterface;
        _fragment = new Wh40kFactionUiFragment();
        _fragment.OnAction += (action, faction, user) => Send(new Wh40kFactionUiMessage(action, faction, user));
        _fragment.ProfileSaveRequested += (faction, profile) => Send(new Wh40kFactionUiMessage(Wh40kFactionUiAction.SaveProfile, faction, profile: profile));
        _fragment.PayoutRequested += (faction, recipient, payout) => Send(new Wh40kFactionUiMessage(Wh40kFactionUiAction.Payout, faction, recipient, payout: payout));
        _fragment.SocialRequested += (action, faction, social) => Send(new Wh40kFactionUiMessage(action, faction, social: social));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }

    public override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is Wh40kFactionSnapshotBuiMessage snapshot)
            _fragment?.UpdateSnapshot(snapshot.Snapshot, snapshot.Status);
    }

    private void Send(Wh40kFactionUiMessage message)
    {
        if (_userInterface == null || _fragment == null)
            return;

        _fragment.SetPending(true);
        _userInterface.SendMessage(new CartridgeUiMessage(message));
    }
}
