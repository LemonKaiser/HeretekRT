using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared._WH40K.Progression;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Progression;

public sealed partial class Wh40kPlayerUi : UIFragment
{
    private Wh40kPlayerUiFragment? _fragment;
    private BoundUserInterface? _userInterface;
    private long _revision;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _userInterface = userInterface;
        _fragment = new Wh40kPlayerUiFragment();
        _fragment.OnCharacteristicPurchase += OnCharacteristicPurchase;
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }

    public override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is not Wh40kPlayerSnapshotBuiMessage snapshotMessage)
            return;

        if (snapshotMessage.Snapshot != null)
            _revision = snapshotMessage.Snapshot.Revision;
        _fragment?.UpdateSnapshot(snapshotMessage.Snapshot, snapshotMessage.Status);
    }

    private void OnCharacteristicPurchase(List<Wh40kCharacteristicAllocation> allocations)
    {
        if (_userInterface == null || _fragment == null)
            return;

        _fragment.SetPending(true);
        _userInterface.SendMessage(new CartridgeUiMessage(
            new Wh40kSpendCharacteristicsUiMessage(_revision, allocations)));
    }
}
