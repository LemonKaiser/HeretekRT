using Content.Client.Eui;
using Content.Shared._WH40K.Administration.Events;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._WH40K.Administration.Events;

[UsedImplicitly]
public sealed class EventAdminEui : BaseEui
{
    private readonly EventAdminWindow _window = new();
    public EventAdminEui()
    {
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.ActionRequested += request => SendMessage(request);
    }
    public override void Opened() => _window.OpenCentered();
    public override void Closed() => _window.Close();
    public override void HandleState(EuiStateBase state)
    {
        if (state is EventAdminEuiState events) _window.Apply(events);
    }
}
