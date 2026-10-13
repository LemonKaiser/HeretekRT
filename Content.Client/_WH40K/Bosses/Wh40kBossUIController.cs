using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Bosses;

public sealed partial class Wh40kBossUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IEyeManager _eyes = default!;
    [UISystemDependency] private readonly Wh40kBossSystem? _bosses = default!;
    [UISystemDependency] private readonly TransformSystem? _transform = default!;
    private Wh40kBossHudControl? _hud;

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
        _bosses?.Clear();
    }

    private void Attach()
    {
        if (UIManager.ActiveScreen is not InGameScreen screen)
            return;
        _hud ??= new Wh40kBossHudControl();
        screen.AttachBossHud(_hud);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_hud == null || UIManager.ActiveScreen is not InGameScreen screen)
            return;
        var boss = _bosses?.Boss;
        _hud.Visible = screen.GameplayHudVisible && boss != null;
        _hud.State = boss;
        if (boss == null || _transform == null || _eyes.CurrentEye is not { } eye ||
            _players.LocalEntity is not { } player ||
            !_entities.TryGetComponent<TransformComponent>(player, out var xform) ||
            xform.MapUid is not { } map || _entities.GetNetEntity(map) != boss.Map)
        {
            _hud.Visible = false;
            return;
        }
        var delta = boss.Position - _transform.GetWorldPosition(xform);
        var rotated = eye.Rotation.RotateVec(delta);
        _hud.ScreenDirection = new Vector2(rotated.X, -rotated.Y);
        _hud.Distance = delta.Length();
    }
}
