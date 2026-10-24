using Content.Shared._WH40K.FactionStorage;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.FactionStorage;

[UsedImplicitly]
public sealed class FactionDepotBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FactionDepotPanel? _window;

    protected override void Open()
    {
        base.Open();
        if (UiKey is not FactionStorageUiKey)
        {
            Close();
            return;
        }
        _window = this.CreateWindowCenteredLeft<FactionDepotPanel>();
        _window.Transfer += (slot, revision, deposit) => SendMessage(new FactionDepotTransfer(slot, revision, deposit));
        _window.Refresh += () => SendMessage(new FactionDepotRefresh());
        if (State is FactionDepotState state)
            _window.Apply(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is FactionDepotState depot)
            _window?.Apply(depot);
    }
}
