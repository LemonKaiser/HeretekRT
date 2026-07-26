using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Visual treatment for the onboarding navigation cards.
/// </summary>
internal sealed class OnboardingAppearanceCardStyleBox : StyleBox
{
    private const float SelectedGradientStop = 0.74f;
    private const float PersistentAccentGradientStop = 1f;
    private const float PersistentAccentOpacity = 0.30f;
    private static readonly Color SelectedGradientLeftColor = Color.FromHex("#4E3C16C2");
    private static readonly Color SelectedGradientRightColor = Color.FromHex("#0C0D0AEB");
    private static readonly ProtoId<ShaderPrototype> SelectedBackgroundShaderId = "HeretekOnboardingAppearanceCard";

    private readonly ShaderInstance _selectedBackgroundShader;

    public float HoverAmount { get; set; }
    public bool Selected { get; }
    public Color? PersistentAccentColor { get; }

    public OnboardingAppearanceCardStyleBox(bool selected, Color? persistentAccentColor = null)
    {
        Selected = selected;
        PersistentAccentColor = persistentAccentColor;
        _selectedBackgroundShader = IoCManager.Resolve<IPrototypeManager>()
            .Index(SelectedBackgroundShaderId)
            .InstanceUnique();
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        if (box.Width <= 0f || box.Height <= 0f)
            return;

        var hovered = Math.Clamp(HoverAmount, 0f, 1f);
        // A card being inspected must always use the normal gold selected state.
        // The green/red mark only denotes an already applied trait and therefore
        // stays behind the active selection instead of competing with it.
        var persistentAccent = Selected ? null : PersistentAccentColor;
        var selected = Selected;
        var effectiveHover = hovered;

        if (selected)
        {
            // CSS uses one horizontal linear-gradient for the selected card.
            // Keep it as one draw call instead of approximating it with many quads.
            var previousShader = handle.GetShader();
            _selectedBackgroundShader.SetParameter("LeftColor", SelectedGradientLeftColor);
            _selectedBackgroundShader.SetParameter("RightColor", SelectedGradientRightColor);
            _selectedBackgroundShader.SetParameter("FadeStop", SelectedGradientStop);
            handle.UseShader(_selectedBackgroundShader);
            handle.DrawRect(box, Color.White);
            handle.UseShader(previousShader);
        }
        else
        {
            handle.DrawRect(box, Lerp(Color.FromHex("#070806D6"), Color.FromHex("#2F2714C4"), effectiveHover));
        }

        var border = selected
            ? Color.FromHex("#E5C879D6")
            : Lerp(Color.FromHex("#B6975447"), Color.FromHex("#E5C879BD"), effectiveHover);
        var borderThickness = MathF.Max(1f, uiScale);
        DrawBorder(handle, box, borderThickness, border);

        if (persistentAccent is { } accent)
        {
            DrawPersistentAccent(handle, box, uiScale, accent);
        }
        else
        {
            var accentAlpha = selected ? 1f : effectiveHover;
            if (accentAlpha > 0f)
            {
                var hoverAccent = selected ? Color.FromHex("#E5C879") : Color.FromHex("#B69754");
                var accentX = box.Left + 7f * uiScale;
                var accentTop = box.Top + 7f * uiScale;
                var accentHeight = MathF.Max(0f, box.Height - 14f * uiScale);

                var accentWidth = MathF.Max(1f, 2f * uiScale);
                handle.DrawRect(
                    UIBox2.FromDimensions(new Vector2(accentX, accentTop), new Vector2(accentWidth, accentHeight)),
                    hoverAccent.WithAlpha(accentAlpha));
            }
        }

        var lineLeft = box.Left + (selected ? 23f : 22f) * uiScale;
        var lineRight = box.Right - (selected ? 18f : 12f) * uiScale;
        var lineThickness = MathF.Max(1f, uiScale);
        var lineY = box.Bottom - 7f * uiScale - lineThickness;
        var lineColor = selected
            ? Color.FromHex("#E5C879")
            : Color.FromHex("#B69754").WithAlpha(0.19f);
        handle.DrawRect(
            UIBox2.FromDimensions(
                new Vector2(lineLeft, lineY),
                new Vector2(MathF.Max(0f, lineRight - lineLeft), lineThickness)),
            lineColor);
    }

    private void DrawPersistentAccent(DrawingHandleScreen handle, UIBox2 box, float uiScale, Color accent)
    {
        var previousShader = handle.GetShader();
        _selectedBackgroundShader.SetParameter("LeftColor", accent.WithAlpha(PersistentAccentOpacity));
        _selectedBackgroundShader.SetParameter("RightColor", accent.WithAlpha(0f));
        _selectedBackgroundShader.SetParameter("FadeStop", PersistentAccentGradientStop);
        handle.UseShader(_selectedBackgroundShader);
        handle.DrawRect(box, Color.White);
        handle.UseShader(previousShader);

        var barX = box.Left + 7f * uiScale;
        var barTop = box.Top + 7f * uiScale;
        var barHeight = MathF.Max(0f, box.Height - 14f * uiScale);
        handle.DrawRect(
            UIBox2.FromDimensions(
                new Vector2(barX, barTop),
                new Vector2(MathF.Max(1f, 2f * uiScale), barHeight)),
            accent.WithAlpha(0.72f));
    }

    protected override float GetDefaultContentMargin(Margin margin)
    {
        return margin switch
        {
            Margin.Left => 17f,
            Margin.Right => 15f,
            Margin.Top => 10f,
            Margin.Bottom => 10f,
            _ => 10f,
        };
    }

    private static Color Lerp(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new Color(
            from.R + (to.R - from.R) * amount,
            from.G + (to.G - from.G) * amount,
            from.B + (to.B - from.B) * amount,
            from.A + (to.A - from.A) * amount);
    }

    private static void DrawBorder(DrawingHandleScreen handle, UIBox2 box, float thickness, Color color)
    {
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(box.Left, box.Top), new Vector2(box.Width, thickness)),
            color);
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(box.Left, box.Bottom - thickness), new Vector2(box.Width, thickness)),
            color);
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(box.Left, box.Top), new Vector2(thickness, box.Height)),
            color);
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(box.Right - thickness, box.Top), new Vector2(thickness, box.Height)),
            color);
    }

}
