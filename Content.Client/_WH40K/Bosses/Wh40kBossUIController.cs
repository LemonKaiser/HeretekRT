using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Bosses;

public sealed partial class Wh40kBossUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [UISystemDependency] private readonly Wh40kBossSystem? _bosses = default!;
    [UISystemDependency] private readonly TransformSystem? _transform = default!;
    [UISystemDependency] private readonly SpriteSystem? _sprites = default!;
    private Wh40kBossHudControl? _hud;
    private NetEntity? _trackedBoss;
    private Vector2 _bossPosition;
    private float _arrowAngle;

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
        _trackedBoss = null;
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
        _hud.ShowPointer = false;
        var boss = _bosses?.Boss;
        _hud.Visible = screen.GameplayHudVisible && boss != null;
        _hud.State = boss;
        if (boss == null || _transform == null ||
            _players.LocalEntity is not { } player ||
            !_entities.TryGetComponent<TransformComponent>(player, out var xform) ||
            xform.MapUid is not { } map || _entities.GetNetEntity(map) != boss.Map)
        {
            _hud.Visible = false;
            return;
        }
        var viewport = screen.GetWidget<MainViewport>()?.Viewport;
        if (viewport?.Eye is not { } viewportEye || viewport.PixelSize.X <= 0 || viewport.PixelSize.Y <= 0 ||
            viewportEye.Position.MapId != xform.MapID)
            return;
        var worldToScreen = viewport.GetWorldToScreenMatrix();
        var localToScreen = viewport.GetLocalToScreenMatrix();
        var visible = Wh40kBossPointerGeometry.VisibleViewport(
            UIBox2.FromDimensions(viewport.GlobalPixelPosition, viewport.PixelSize),
            (Vector2) viewport.ViewportSize * viewport.CurrentRenderScale, localToScreen);

        var position = boss.Position;
        Box2Rotated? spriteBounds = null;
        // Client transforms already include render interpolation. Detached PVS entities must use the snapshot.
        if (_entities.TryGetEntity(boss.Boss, out var localBoss) &&
            _entities.TryGetComponent<TransformComponent>(localBoss, out var bossTransform) && bossTransform.MapUid == map &&
            (_entities.GetComponent<MetaDataComponent>(localBoss.Value).Flags & MetaDataFlags.Detached) == 0)
        {
            position = _transform.GetWorldPosition(bossTransform);
            if (_sprites != null && _entities.TryGetComponent<SpriteComponent>(localBoss, out var sprite))
                spriteBounds = _sprites.CalculateBounds((localBoss.Value, sprite), position,
                    _transform.GetWorldRotation(bossTransform), viewportEye.Rotation);
        }
        var projected = Wh40kBossPointerGeometry.Project(spriteBounds ?? new Box2Rotated(new Box2(position, position)), worldToScreen);
        _hud.ShowPointer = Wh40kBossPointerGeometry.ShowPointer(visible, projected);
        _hud.PointerBounds = visible.Translated(-_hud.GlobalPixelPosition);

        var changed = _trackedBoss != boss.Boss;
        if (changed || Vector2.DistanceSquared(_bossPosition, position) > 64)
            _bossPosition = position;
        else
            _bossPosition = Vector2.Lerp(_bossPosition, position, 1 - MathF.Exp(-12 * args.DeltaSeconds));
        _trackedBoss = boss.Boss;
        var delta = _bossPosition - _transform.GetWorldPosition(xform);
        var screenDelta = Vector2.Transform(_bossPosition, worldToScreen) - visible.Center;
        var angle = MathF.Atan2(screenDelta.Y, screenDelta.X);
        var difference = MathF.Atan2(MathF.Sin(angle - _arrowAngle), MathF.Cos(angle - _arrowAngle));
        _arrowAngle = changed ? angle : _arrowAngle + difference * (1 - MathF.Exp(-18 * args.DeltaSeconds));
        _hud.ScreenDirection = new Vector2(MathF.Cos(_arrowAngle), MathF.Sin(_arrowAngle));
        _hud.Distance = delta.Length();
    }
}
