using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WH40K.Administration.Events;
using Content.Shared.Eui;

namespace Content.Server._WH40K.Administration.Events;

public sealed class EventAdminEui : BaseEui
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private ILogManager _log = default!;
    private EventAdminSystem System => _entities.System<EventAdminSystem>();
    private EventAdminResult _result = new();
    private bool _busy;
    private bool _closed;
    private int _completedRequest;

    public EventAdminEui() => IoCManager.InjectDependencies(this);

    public override void Opened()
    {
        System.Changed += OnChanged;
        _admins.OnPermsChanged += OnPermsChanged;
        StateDirty();
    }

    public override void Closed()
    {
        _closed = true;
        System.Changed -= OnChanged;
        _admins.OnPermsChanged -= OnPermsChanged;
    }

    private void OnChanged()
    {
        if (_closed || IsShutDown) return;
        if (!System.Authorized(Player)) { Close(); return; }
        StateDirty();
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player != Player || _closed) return;
        if (!System.Authorized(Player)) Close();
        else StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        var state = System.Snapshot(Player);
        return new EventAdminEuiState
        {
            Epoch = state.Epoch, CompletedRequest = _completedRequest, Players = state.Players, AllowedActions = state.AllowedActions,
            Teams = state.Teams, Points = state.Points,
            SecondsLeft = state.SecondsLeft, TimerRunning = state.TimerRunning, Busy = _busy, Result = _result,
        };
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (_closed || IsShutDown || msg is not EventAdminRequest request) return;
        if (!System.Authorized(Player)) { Close(); return; }
        if (_busy || request.RequestId <= _completedRequest) return;
        _ = Execute(request);
    }

    private async Task Execute(EventAdminRequest request)
    {
        _busy = true;
        StateDirty();
        try { _result = await System.Execute(Player, request); }
        catch (Exception exception)
        {
            _log.GetSawmill("event.admin").Error($"Event action {request.Action} by {Player.UserId}: {exception}");
            _result = new("wh40k-event-failed");
        }
        finally
        {
            _busy = false;
            _completedRequest = request.RequestId;
            if (!_closed && !IsShutDown) StateDirty();
        }
    }
}
