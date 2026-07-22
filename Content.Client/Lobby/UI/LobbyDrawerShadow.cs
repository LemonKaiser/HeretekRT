using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>
/// A separate sibling surface for the drawer's soft exterior shadow.
/// The drawer itself clips its contents, so this deliberately lives behind it.
/// </summary>
public sealed class LobbyDrawerShadow : Control
{
    private static readonly ProtoId<ShaderPrototype> ShadowShaderId = "HeretekLobbyDrawerShadow";

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private readonly ShaderInstance _shadowShader;

    public float DrawerWidth { get; set; } = 520f;
    public float Opacity { get; set; }

    public LobbyDrawerShadow()
    {
        IoCManager.InjectDependencies(this);
        _shadowShader = _prototypeManager.Index(ShadowShaderId).InstanceUnique();
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        var panelWidth = DrawerWidth * UIScale;
        var shadowWidth = MathF.Min(64f * UIScale, MathF.Max(0f, size.X - panelWidth));
        if (shadowWidth <= 0.5f || Opacity <= 0.001f)
            return;

        _shadowShader.SetParameter("Opacity", Math.Clamp(Opacity, 0f, 1f));
        _shadowShader.SetParameter("ShadowFraction", shadowWidth / size.X);

        var previousShader = handle.GetShader();
        handle.UseShader(_shadowShader);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, size), Color.White);
        handle.UseShader(previousShader);
    }
}
