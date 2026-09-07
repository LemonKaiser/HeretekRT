using Content.Server.EUI;
using Content.Shared._WH40K.DetailExaminable;
using Content.Shared.Eui;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.DetailExaminable;

public sealed class Wh40kDetailExaminableEui : BaseEui
{
    private readonly Wh40kDetailExaminableEuiState _state;
    private readonly Action<Wh40kDetailExaminableEui> _onClosed;

    public EntityUid Target { get; }

    public Wh40kDetailExaminableEui(
        Wh40kDetailExaminableEuiState state,
        EntityUid target,
        Action<Wh40kDetailExaminableEui> onClosed)
    {
        _state = state;
        Target = target;
        _onClosed = onClosed;
    }

    public override EuiStateBase GetNewState()
    {
        return _state;
    }

    public override void Closed()
    {
        _onClosed(this);
        base.Closed();
    }
}
