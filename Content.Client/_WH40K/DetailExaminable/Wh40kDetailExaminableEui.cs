using System.Numerics;
using Content.Client.Eui;
using Content.Shared._WH40K.DetailExaminable;
using Content.Shared.Eui;

namespace Content.Client._WH40K.DetailExaminable;

public sealed class Wh40kDetailExaminableEui : BaseEui
{
    private readonly Wh40kDetailExaminableWindow _window = new();

    public override void Opened()
    {
        _window.OpenCenteredAt(new Vector2(0f, 0.75f));
    }

    public override void Closed()
    {
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is Wh40kDetailExaminableEuiState detail)
            _window.UpdateState(detail);
    }
}
