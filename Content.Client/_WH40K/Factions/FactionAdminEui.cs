using Content.Client.Eui;
using Content.Shared._WH40K.Factions;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._WH40K.Factions;

[UsedImplicitly]
public sealed class FactionAdminEui : BaseEui
{
    private readonly FactionAdminWindow _window = new();

    public FactionAdminEui()
    {
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.FactionSelected += id => SendMessage(new FactionAdminSelect(id));
        _window.ActionRequested += request => SendMessage(request);
    }

    public override void Opened() => _window.OpenCentered();
    public override void Closed() => _window.Close();

    public override void HandleState(EuiStateBase state)
    {
        if (state is FactionAdminEuiState factions)
            _window.Apply(factions);
    }
}
