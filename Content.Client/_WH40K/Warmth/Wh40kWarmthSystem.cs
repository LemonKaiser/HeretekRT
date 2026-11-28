using System.Numerics;
using Content.Client._Shitmed.UserInterface.Systems.Targeting.Widgets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Shared._WH40K.Warmth;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Warmth;

/// <summary>
/// Client thermal HUD and frost/heat edge overlay controller.
/// </summary>
public sealed partial class Wh40kWarmthSystem : SharedWh40kWarmthSystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private const float RefreshInterval = 0.25f;
    private const float HideDelay = 3f;
    private const float WetShowThreshold = 0.2f;
    private const float WetHideThreshold = 0.15f;
    private const int AirRecoveryMargin = 3;
    private const float HudGap = 8f;

    private Wh40kWarmthOverlay _overlay = default!;
    private Wh40kWarmthHud _hud = default!;
    private float _untilRefresh;
    private float _visibleFor;
    private EntityUid? _hudBody;
    private bool _coldAir;
    private bool _hotAir;
    private bool _damp;

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;
        base.Initialize();
        SubscribeLocalEvent<Wh40kWarmthComponent, AfterAutoHandleStateEvent>(OnState);

        _overlay = new Wh40kWarmthOverlay();
        _overlays.AddOverlay(_overlay);

        _hud = new Wh40kWarmthHud(_resources) { Visible = false };
        _ui.PopupRoot.AddChild(_hud);
        LayoutContainer.SetAnchorPreset(_hud, LayoutContainer.LayoutPreset.TopLeft);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_overlay);
        _hud.Orphan();
        base.Shutdown();
    }

    private void OnState(Entity<Wh40kWarmthComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Movement.RefreshMovementSpeedModifiers(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _visibleFor -= frameTime;
        _untilRefresh -= frameTime;
        if (_untilRefresh > 0f)
            return;

        _untilRefresh = RefreshInterval;
        var body = _player.LocalEntity;
        if (body != _hudBody)
        {
            _hudBody = body;
            ResetVisibility();
        }

        if (_hudBody == null || !TryComp(_hudBody, out Wh40kWarmthComponent? warmth) || !warmth.HasReading)
        {
            ResetVisibility();
            return;
        }

        UpdateVisibility(warmth);
        if (!_hud.Visible)
            return;

        _hud.UpdateReadout(warmth);
        PositionHud();
    }

    private void UpdateVisibility(Wh40kWarmthComponent warmth)
    {
        var here = warmth.HereCelsius;
        _coldAir = here <= Wh40kWarmthHud.ColdAirThreshold + (_coldAir ? AirRecoveryMargin : 0);
        _hotAir = here >= Wh40kWarmthHud.HotAirThreshold - (_hotAir ? AirRecoveryMargin : 0);
        _damp = warmth.Wetness >= (_damp ? WetHideThreshold : WetShowThreshold);

        if (here == null || _coldAir || _hotAir || _damp || warmth.Strain != Wh40kWarmthStrain.None)
            _visibleFor = HideDelay;

        _hud.Visible = _visibleFor > 0f;
    }

    private void ResetVisibility()
    {
        _coldAir = false;
        _hotAir = false;
        _damp = false;
        _visibleFor = 0f;
        _hud.Visible = false;
    }

    private void PositionHud()
    {
        var root = _ui.PopupRoot;
        var size = new Vector2(
            MathF.Max(_hud.Width, Wh40kWarmthHud.PanelWidth),
            MathF.Max(_hud.Height, Wh40kWarmthHud.PanelHeight));

        var area = UIBox2.FromDimensions(Vector2.Zero, root.Size);
        if (_ui.ActiveScreen?.GetWidget<MainViewport>() is { Visible: true, Width: > 0, Height: > 0 } viewport)
            area = UIBox2.FromDimensions(viewport.GlobalPosition - root.GlobalPosition, viewport.Size);

        var right = area.Right - 12f;
        var bottom = area.Bottom - 150f;
        if (_ui.GetActiveUIWidgetOrNull<TargetingControl>() is { Visible: true, Width: > 0 } target)
        {
            var position = target.GlobalPosition - root.GlobalPosition;
            right = position.X + target.Width;
            bottom = position.Y - HudGap;
        }

        right = MathF.Min(right, area.Right);
        var left = MathF.Max(area.Left, right - size.X);
        var top = MathF.Max(area.Top, bottom - size.Y);

        if (_ui.GetActiveUIWidgetOrNull<AlertsUI>() is { Visible: true } alerts)
        {
            foreach (var child in alerts.Children)
            {
                if (!child.Visible)
                    continue;

                var box = UIBox2.FromDimensions(child.GlobalPosition - root.GlobalPosition, child.Size);
                var panel = UIBox2.FromDimensions(left, top, size.X, size.Y);
                if (box.Intersects(panel))
                    left = MathF.Max(area.Left, box.Left - HudGap - size.X);
            }
        }

        LayoutContainer.SetPosition(_hud, new Vector2(left, top));
    }
}
