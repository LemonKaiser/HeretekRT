using System.Linq;
using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client.PDA;
using Content.Client.UserInterface.Controls;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.UserInterface;

/// <summary>One reduced-resolution blur shared by all windows; never renders the game world again.</summary>
internal sealed class Wh40kGlassPass : Control
{
    private static readonly ProtoId<ShaderPrototype> FrostedGlassShader = "Wh40kUiFrostedGlass";
    private readonly IUserInterfaceManager _ui = IoCManager.Resolve<IUserInterfaceManager>();
    private readonly IStateManager _states = IoCManager.Resolve<IStateManager>();
    private readonly IConfigurationManager _cfg = IoCManager.Resolve<IConfigurationManager>();
    private readonly IClyde _clyde = IoCManager.Resolve<IClyde>();
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1d / 30);
    private TimeSpan _nextRefresh;
    private Robust.Client.State.State? _sourceState;
    private Texture? _staticBackdrop;
    private UIBox2i _staticBackdropBox;
    private IRenderTexture? _source;
    private IRenderTexture? _horizontal;
    private IRenderTexture? _vertical;
    private ShaderInstance? _blurX;
    private ShaderInstance? _blurY;
    internal Texture? Texture { get; private set; }
    internal Vector2i ScreenSize { get; private set; }

    internal Wh40kGlassPass()
    {
        MouseFilter = MouseFilterMode.Ignore;
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
    }

    protected override void Draw(IRenderHandle render)
    {
        var visibleWindow = _ui.WindowRoot.Children.Concat(_ui.ModalRoot.Children).OfType<BaseWindow>()
            .Any(window => window.Visible && window is not (PdaWindow or RadialMenu));
        var visiblePanel = _states.CurrentState is LobbyState or GameplayState &&
            _ui.GetUIController<Wh40kInterfaceUIController>().HasVisibleFloatingPanels;
        if (!_cfg.GetCVar(CCVars.WH40KUiFrostedGlass) || _cfg.GetCVar(CCVars.WH40KUiGlassTransparency) <= 0 ||
            (!visibleWindow && !visiblePanel))
        {
            ReleaseTargets();
            return;
        }
        var size = _ui.RootControl.PixelSize;
        if (size.X <= 0 || size.Y <= 0) return;
        var state = _states.CurrentState;
        // Do this before reusing a cached frame: vision effects may have changed since the last refresh.
        if (state is GameplayState { Viewport: { } view } &&
            (view.Viewport.Shader != null || view.Viewport.RenderedTexture == null))
        {
            ReleaseTargets();
            return;
        }
        Texture? staticBackdrop = null;
        var backdropBox = default(UIBox2i);
        if (state is LobbyState { Lobby: { IsAnimatedLobbyBackground: false } staticLobby })
        {
            staticBackdrop = staticLobby.Background.Texture;
            backdropBox = UIBox2i.FromDimensions(staticLobby.Background.GlobalPixelPosition, staticLobby.Background.PixelSize);
        }
        if (Texture != null && ScreenSize == size && ReferenceEquals(_sourceState, state))
        {
            // A static lobby image needs one blur, not continuous offscreen rendering.
            if (staticBackdrop != null && ReferenceEquals(_staticBackdrop, staticBackdrop) && _staticBackdropBox.Equals(backdropBox))
                return;
            if (staticBackdrop == null && _staticBackdrop == null && _timing.RealTime < _nextRefresh)
                return;
        }
        // Bound both bandwidth and memory even on high-resolution displays.
        var divisor = Math.Max(4, (int) Math.Ceiling(Math.Max(size.X / 640f, size.Y / 360f)));
        var reduced = Vector2i.ComponentMax(new Vector2i(1, 1), size / divisor);
        if (_source?.Size != reduced)
        {
            ReleaseTargets();
            var sampling = new TextureSampleParameters { Filter = true };
            _source = _clyde.CreateRenderTarget(reduced, RenderTargetColorFormat.Rgba8Srgb, sampling, "Heretek-Glass-Source");
            _horizontal = _clyde.CreateRenderTarget(reduced, RenderTargetColorFormat.Rgba8Srgb, sampling, "Heretek-Glass-X");
            _vertical = _clyde.CreateRenderTarget(reduced, RenderTargetColorFormat.Rgba8Srgb, sampling, "Heretek-Glass-Y");
        }
        if (_blurX == null)
        {
            var prototype = IoCManager.Resolve<IPrototypeManager>().Index(FrostedGlassShader);
            _blurX = prototype.Instance().Duplicate(); _blurY = prototype.Instance().Duplicate();
            _blurX.SetParameter("BlurDirection", new Vector2(1.2f, 0));
            _blurY.SetParameter("BlurDirection", new Vector2(0, 1.2f));
        }
        var draw = render.DrawingHandleScreen;
        var transform = draw.GetTransform();
        var modulate = draw.Modulate;
        draw.SetTransform(Matrix3x2.Identity); draw.Modulate = Color.White;
        render.SetScissor(null);
        try
        {
            render.RenderInRenderTarget(_source!, () =>
            {
                var target = UIBox2.FromDimensions(Vector2.Zero, reduced);
                if (_states.CurrentState is GameplayState { Viewport: { } viewport })
                {
                    // Viewport post-effects can conceal information; never expose its raw frame through glass.
                    if (viewport.Viewport.Shader != null || viewport.Viewport.RenderedTexture is not { } world) return;
                    var rect = viewport.Viewport.GlobalDrawBox;
                    var scale = (Vector2) reduced / size;
                    draw.DrawTextureRect(world, new UIBox2(rect.TopLeft * scale, rect.BottomRight * scale));
                }
                else if (_states.CurrentState is LobbyState { Lobby: { } lobby } && lobby.Background.Texture is { } backdrop)
                {
                    var scene = (Vector2) lobby.Background.PixelSize;
                    var factor = MathF.Max(scene.X / backdrop.Width, scene.Y / backdrop.Height);
                    var drawn = (Vector2) backdrop.Size * factor;
                    var scale = (Vector2) reduced / size;
                    var origin = ((Vector2) lobby.Background.GlobalPixelPosition + (scene - drawn) / 2) * scale;
                    draw.DrawTextureRect(backdrop, UIBox2.FromDimensions(origin, drawn * scale));
                }
                else
                {
                    // Menu parallax and controls are cheap 2D content. WindowRoot is separate, avoiding feedback.
                    var scale = (Vector2) reduced / size;
                    var coordinate = Matrix3x2.CreateScale(scale);
                    draw.SetTransform(coordinate);
                    var count = 0;
                    var args = new ControlRenderArguments
                    {
                        Handle = render, Total = ref count, Position = Vector2i.Zero,
                        Modulate = Color.White, ScissorBox = null, CoordinateTransform = ref coordinate,
                    };
                    _ui.RenderControl(args, _ui.StateRoot);
                    draw.SetTransform(Matrix3x2.Identity);
                    render.SetScissor(null);
                }
            }, Color.FromHex("#17130F"));
            void Blur(IRenderTexture source, IRenderTexture destination, ShaderInstance shader)
            {
                render.RenderInRenderTarget(destination, () =>
                {
                    draw.UseShader(shader);
                    draw.DrawTextureRect(source.Texture, UIBox2.FromDimensions(Vector2.Zero, reduced));
                    draw.UseShader(null);
                }, Color.Transparent);
            }
            Blur(_source!, _horizontal!, _blurX);
            Blur(_horizontal!, _vertical!, _blurY!);
            Texture = _vertical!.Texture; ScreenSize = size;
            _sourceState = state; _staticBackdrop = staticBackdrop; _staticBackdropBox = backdropBox;
            _nextRefresh = _timing.RealTime + RefreshInterval;
        }
        finally
        {
            draw.UseShader(null); draw.Modulate = modulate; draw.SetTransform(transform);
        }
    }

    private void ReleaseTargets()
    {
        Texture = null;
        _sourceState = null; _staticBackdrop = null; _nextRefresh = default;
        _source?.Dispose(); _horizontal?.Dispose(); _vertical?.Dispose();
        _source = null; _horizontal = null; _vertical = null;
    }

    protected override void ExitedTree()
    {
        ReleaseTargets();
        base.ExitedTree();
    }
}
