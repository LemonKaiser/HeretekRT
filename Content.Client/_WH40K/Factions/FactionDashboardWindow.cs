using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Progression;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Factions;

public sealed class FactionDashboardWindow : FancyWindow
{
    public readonly FactionDashboard Dashboard = new() { Margin = new Thickness(18) };
    private bool? _hasFaction;

    public FactionDashboardWindow()
    {
        Title = Loc.GetString("wh40k-faction-window-title");
        MinSize = new Vector2(640, 480);
        SetSize = new Vector2(820, 590);
        ContentsContainer.AddChild(Dashboard);
    }

    public void UpdateSnapshot(Wh40kFactionUiSnapshot? snapshot, Wh40kFactionUiOperationStatus status, bool pending)
    {
        Dashboard.UpdateSnapshot(snapshot, status, pending);
        var hasFaction = snapshot?.FactionId != null;
        if (_hasFaction == hasFaction)
            return;

        _hasFaction = hasFaction;
        var size = hasFaction ? new Vector2(880, 680) : new Vector2(820, 590);
        var viewport = UserInterfaceManager.WindowRoot.Size;
        if (viewport.X >= MinWidth && viewport.Y >= MinHeight)
            size = Vector2.Min(size, viewport);
        SetSize = size;
        if (Parent == UserInterfaceManager.WindowRoot)
        {
            UserInterfaceManager.DeferAction(() =>
            {
                if (Parent != UserInterfaceManager.WindowRoot)
                    return;
                Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
                RecenterWindow(new Vector2(0.5f, 0.5f));
            });
        }
    }
}
