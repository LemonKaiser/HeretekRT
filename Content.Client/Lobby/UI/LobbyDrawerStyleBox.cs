using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Layered right-drawer surface used instead of a flat opaque panel.
/// </summary>
internal sealed class LobbyDrawerStyleBox : StyleBox
{
    private static readonly ProtoId<ShaderPrototype> ShadeShaderId = "HeretekLobbyDrawerShade";

    public Color LeftColor { get; set; }
    public Color MiddleColor { get; set; }
    public Color RightColor { get; set; }
    public Color BorderColor { get; set; }
    public Thickness BorderThickness { get; set; }

    private readonly ShaderInstance _shadeShader;

    public LobbyDrawerStyleBox()
    {
        _shadeShader = CreateShadeShader();
    }

    public LobbyDrawerStyleBox(LobbyDrawerStyleBox other)
        : base(other)
    {
        _shadeShader = CreateShadeShader();
        LeftColor = other.LeftColor;
        MiddleColor = other.MiddleColor;
        RightColor = other.RightColor;
        BorderColor = other.BorderColor;
        BorderThickness = other.BorderThickness;
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        if (box.Width <= 0f || box.Height <= 0f)
            return;

        // Render the dark glass in one pass. The previous sequence of translucent
        // rectangles produced seams that were visible over pale lobby artwork.
        var previousShader = handle.GetShader();
        _shadeShader.SetParameter("LeftColor", LeftColor);
        _shadeShader.SetParameter("MiddleColor", MiddleColor);
        _shadeShader.SetParameter("RightColor", RightColor);
        handle.UseShader(_shadeShader);
        handle.DrawRect(box, Color.White);
        handle.UseShader(previousShader);

        var border = BorderThickness.Scale(uiScale);
        if (border.Left > 0f)
        {
            handle.DrawRect(
                UIBox2.FromDimensions(new Vector2(box.Left, box.Top), new Vector2(border.Left, box.Height)),
                BorderColor);
        }
    }

    protected override float GetDefaultContentMargin(Margin margin)
    {
        return margin switch
        {
            Margin.Left => BorderThickness.Left,
            Margin.Top => BorderThickness.Top,
            Margin.Right => BorderThickness.Right,
            Margin.Bottom => BorderThickness.Bottom,
            _ => 0f,
        };
    }

    private static ShaderInstance CreateShadeShader()
    {
        return IoCManager.Resolve<IPrototypeManager>().Index(ShadeShaderId).InstanceUnique();
    }
}
