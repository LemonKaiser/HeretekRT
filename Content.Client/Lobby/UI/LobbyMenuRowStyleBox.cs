using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>
/// A thin, fading command-deck row surface. It avoids generic rectangular button chrome
/// while keeping the whole row, including its index, visually coherent.
/// </summary>
internal sealed class LobbyMenuRowStyleBox : StyleBox
{
    private static readonly ProtoId<ShaderPrototype> DividerShaderId = "HeretekLobbyMenuDivider";
    private static readonly ProtoId<ShaderPrototype> BackgroundShaderId = "HeretekLobbyMenuRow";

    private readonly ShaderInstance _dividerShader;
    private readonly ShaderInstance _backgroundShader;

    public Color LeftColor { get; set; }
    public Color MiddleColor { get; set; }
    public Color RightColor { get; set; }
    /// <summary>Where the primary colour transitions into the final transparent tail.</summary>
    public float MiddleStop { get; set; }
    public Color AccentColor { get; set; }
    public bool DrawAccent { get; set; }
    public bool DrawDivider { get; set; } = true;

    public LobbyMenuRowStyleBox()
    {
        var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        _dividerShader = prototypeManager.Index(DividerShaderId).Instance();
        _backgroundShader = prototypeManager.Index(BackgroundShaderId).InstanceUnique();
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var width = box.Width;
        if (width <= 0f || box.Height <= 0f)
            return;

        // Most rows are fully transparent while idle. For visible rows the same
        // three-stop gradient is evaluated per pixel in one draw call. The earlier
        // 32-quad approximation made its individual bands visible on wide rows.
        if (LeftColor.A > 0f || MiddleColor.A > 0f || RightColor.A > 0f)
        {
            DrawBackgroundGradient(handle, box);
        }

        if (DrawAccent)
        {
            var x = box.Left + 7f * uiScale;
            var top = box.Top + 10f * uiScale;
            var height = MathF.Max(0f, box.Height - 20f * uiScale);
            handle.DrawRect(
                UIBox2.FromDimensions(new Vector2(x, top), new Vector2(MathF.Max(1f, uiScale), height)),
                AccentColor);
            handle.DrawRect(
                UIBox2.FromDimensions(new Vector2(x - uiScale, top), new Vector2(3f * uiScale, height)),
                AccentColor.WithAlpha(AccentColor.A * 0.16f));
        }

        if (DrawDivider)
            DrawDividerGradient(handle, box, uiScale);
    }

    protected override float GetDefaultContentMargin(Margin margin) => 0f;

    private void DrawDividerGradient(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var height = MathF.Max(1f, uiScale);
        var previousShader = handle.GetShader();
        handle.UseShader(_dividerShader);
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(box.Left, box.Bottom - height), new Vector2(box.Width, height)),
            Color.White);
        handle.UseShader(previousShader);
    }

    private void DrawBackgroundGradient(DrawingHandleScreen handle, UIBox2 box)
    {
        var previousShader = handle.GetShader();
        _backgroundShader.SetParameter("LeftColor", LeftColor);
        _backgroundShader.SetParameter("MiddleColor", MiddleColor);
        _backgroundShader.SetParameter("RightColor", RightColor);
        _backgroundShader.SetParameter("MiddleStop", MiddleStop);
        handle.UseShader(_backgroundShader);
        handle.DrawRect(box, Color.White);
        handle.UseShader(previousShader);
    }
}
