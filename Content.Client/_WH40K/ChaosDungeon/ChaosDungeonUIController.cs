using Content.Client.Gameplay;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WH40K.ChaosDungeon;
using Robust.Client.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.ChaosDungeon;

public sealed partial class ChaosDungeonUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    private Control? _hud;
    private Label? _label;

    public override void Initialize()
    {
        base.Initialize();
        UIManager.GetUIController<GameplayStateLoadController>().OnScreenLoad += Attach;
    }

    public void OnStateEntered(GameplayState state) => Attach();

    public void OnStateExited(GameplayState state)
    {
        _hud?.Orphan();
        _hud = null;
        _label = null;
    }

    private void Attach()
    {
        if (UIManager.ActiveScreen is not InGameScreen screen)
            return;
        if (_hud == null)
        {
            _label = new Label
            {
                HorizontalExpand = true,
                VerticalExpand = true,
                Align = Label.AlignMode.Center,
                VAlign = Label.VAlignMode.Center,
            };
            _hud = new Control
            {
                MouseFilter = Control.MouseFilterMode.Ignore,
                Visible = false,
                Children =
                {
                    new PanelContainer
                    {
                        HorizontalAlignment = Control.HAlignment.Center,
                        VerticalAlignment = Control.VAlignment.Top,
                        Margin = new Thickness(0, 100, 0, 0),
                        PanelOverride = new StyleBoxFlat
                        {
                            BackgroundColor = Color.FromHex("#153A6399"),
                            ContentMarginLeftOverride = 12,
                            ContentMarginRightOverride = 12,
                            ContentMarginTopOverride = 5,
                            ContentMarginBottomOverride = 5,
                        },
                        Children = { _label },
                    },
                },
            };
        }
        screen.AttachBossHud(_hud);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_hud == null || _label == null)
            return;
        if (UIManager.ActiveScreen is not InGameScreen { GameplayHudVisible: true } ||
            _players.LocalEntity is not { } player ||
            !_entities.TryGetComponent<ChaosDungeonTimerComponent>(player, out var timer))
        {
            _hud.Visible = false;
            return;
        }

        _hud.Visible = true;
        var remaining = timer.ExpiresAt - _timing.CurTime;
        _label.Text = Loc.GetString(timer.Extraction ? "chaos-dungeon-extraction-timer" : "chaos-dungeon-timer",
            ("time", ChaosDungeonTiming.FormatCountdown(remaining)));
        _label.FontColorOverride = remaining <= TimeSpan.FromMinutes(5) ? Color.OrangeRed : Color.White;
    }
}
