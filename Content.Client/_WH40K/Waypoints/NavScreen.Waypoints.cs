using Content.Shared.Shuttles.Components;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    public void Wh40kEnableWaypoints() => NavRadar.Wh40kEnableWaypoints(ShuttleConsoleUiKey.Key, canFly: true);
}
