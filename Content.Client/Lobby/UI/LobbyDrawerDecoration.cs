using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Fine animated accents painted above the real SS14 chat without replacing its controls.
/// </summary>
internal sealed class LobbyDrawerDecoration : Control
{
    private static readonly Color Gold = Color.FromHex("#E5C879");
    private static readonly ProtoId<ShaderPrototype> RuleShaderId = "HeretekLobbyDrawerRule";

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    private readonly ShaderInstance _ruleShader;

    public float Pulse { get; set; }

    public LobbyDrawerDecoration()
    {
        IoCManager.InjectDependencies(this);
        _ruleShader = _prototypeManager.Index(RuleShaderId).InstanceUnique();
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        var size = PixelSize;
        if (size.X <= 1f || size.Y <= 1f)
            return;

        var headerY = 86f * scale;
        DrawHeaderRule(handle, size.X, headerY, scale);

        // This control is laid out inside the drawer's 24px content margin. Offset
        // the animated rule back onto the outer drawer frame, away from messages.
        var glow = 0.35f + Pulse * 0.40f;
        // The content box has a bottom inset for the footer. Calculate the highlight
        // against the full drawer instead, otherwise its visual centre is pulled up
        // by that inset.
        var parent = Parent;
        var contentTop = parent == null
            ? 0f
            : GlobalPixelPosition.Y - parent.GlobalPixelPosition.Y;
        var ruleCenter = parent == null
            ? size.Y * 0.5f
            : Math.Clamp(parent.PixelSize.Y * 0.5f - contentTop, 0f, size.Y);
        DrawVerticalRule(handle, -23f * scale, size.Y, ruleCenter, glow, scale);
    }

    private static void DrawHeaderRule(DrawingHandleScreen handle, float width, float y, float scale)
    {
        // The base 1px divider comes from the XAML HLine. This is only the 62px
        // bright segment and its restrained halo from the approved prototype.
        var accentWidth = MathF.Min(width, 62f * scale);
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(0f, y - scale), new Vector2(accentWidth, 4f * scale)),
            Gold.WithAlpha(0.10f));
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(0f, y), new Vector2(accentWidth, 2f * scale)),
            Gold);
    }

    private void DrawVerticalRule(
        DrawingHandleScreen handle,
        float x,
        float height,
        float center,
        float intensity,
        float scale)
    {
        if (height <= 0f)
            return;

        var previousShader = handle.GetShader();
        _ruleShader.SetParameter("Center", Math.Clamp(center / height, 0f, 1f));
        _ruleShader.SetParameter("Intensity", intensity);
        handle.UseShader(_ruleShader);
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(x, 0f), new Vector2(MathF.Max(1f, scale), height)),
            Color.White);
        handle.UseShader(previousShader);
    }

}
