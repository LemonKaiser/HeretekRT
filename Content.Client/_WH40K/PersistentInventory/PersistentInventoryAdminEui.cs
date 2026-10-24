using Content.Client.Eui;
using Content.Shared._WH40K.PersistentInventory;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._WH40K.PersistentInventory;

[UsedImplicitly]
public sealed class PersistentInventoryAdminEui : BaseEui
{
    private readonly PersistentInventoryAdminWindow _window = new();

    public PersistentInventoryAdminEui()
    {
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.PlayerSelected += target => SendMessage(new PersistentInventoryAdminSelect(target));
        _window.SnapshotSelected += (user, snapshot) => SendMessage(new PersistentInventoryAdminInspect(user, snapshot));
        _window.ActionRequested += request => SendMessage(request);
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is PersistentInventoryAdminEuiState inventory)
            _window.Apply(inventory);
    }
}
