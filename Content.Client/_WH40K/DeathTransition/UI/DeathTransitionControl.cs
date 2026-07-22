using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Shared._WH40K.DeathTransition;
using Robust.Shared.IoC;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.DeathTransition.UI;

/// <summary>
/// Full-screen, non-interactive presentation above the HUD while the server completes a death transition.
/// </summary>
public sealed class DeathTransitionControl : Control
{
    private static readonly Color FogColor = Color.FromHex("#7f8b9c");
    private static readonly Color GlowColor = Color.FromHex("#c6d4e8");

    private readonly Texture _fogTexture;
    private readonly Font _titleFont;
    private readonly string _title;
    private TimeSpan _elapsed;

    public DeathTransitionControl()
    {
        var resources = IoCManager.Resolve<IResourceCache>();
        _fogTexture = resources.GetResource<TextureResource>("/Textures/_WH40K/DeathTransition/fog01.png").Texture;
        _titleFont = new VectorFont(
            resources.GetResource<FontResource>("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"),
            56);
        _title = Loc.GetString("heretek-death-transition-title");

        MouseFilter = MouseFilterMode.Stop;
        CanKeyboardFocus = true;
        RectClipContent = true;
    }

    public void SetElapsed(TimeSpan elapsed)
    {
        _elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var presentation = SmoothStep(Math.Clamp(
            (float) (_elapsed / DeathTransitionTiming.ScreenFadeDuration),
            0f,
            1f));
        handle.DrawRect(PixelSizeBox, Color.Black.WithAlpha(presentation * 0.44f));
        DrawFog(handle, size, presentation);

        var titleProgress = SmootherStep(Math.Clamp(
            (float) ((_elapsed - DeathTransitionTiming.ScreenFadeDuration) / DeathTransitionTiming.TitleRevealDuration),
            0f,
            1f));
        if (titleProgress <= 0f)
            return;

        var scale = UIScale;
        var textSize = handle.GetDimensions(_titleFont, _title, scale);
        var position = (size - textSize) / 2f;
        var time = (float) _elapsed.TotalSeconds;
        var flicker = 0.96f + MathF.Sin(time * 4.7f) * 0.025f + MathF.Sin(time * 8.9f) * 0.012f;
        DrawGlowString(handle, position, scale, titleProgress * titleProgress * flicker);
    }

    private void DrawFog(DrawingHandleScreen handle, Vector2 size, float presentation)
    {
        var fogWidth = size.X * 1.22f;
        var offset = fogWidth * Math.Clamp((float) (_elapsed / DeathTransitionTiming.TotalDuration), 0f, 1f) * 0.10f;
        var fogSize = new Vector2(fogWidth, size.Y);
        var fogColor = FogColor.WithAlpha(presentation * 0.075f);

        handle.DrawTextureRect(_fogTexture, UIBox2.FromDimensions(new Vector2(-offset, 0f), fogSize), fogColor);
        handle.DrawTextureRect(_fogTexture, UIBox2.FromDimensions(new Vector2(fogWidth - offset, 0f), fogSize), fogColor);
    }

    private void DrawGlowString(DrawingHandleScreen handle, Vector2 position, float scale, float opacity)
    {
        var outerGlow = GlowColor.WithAlpha(opacity * 0.006f);
        var middleGlow = GlowColor.WithAlpha(opacity * 0.016f);
        var innerGlow = GlowColor.WithAlpha(opacity * 0.045f);
        var outerRadius = 8f * UIScale;
        var middleRadius = 4f * UIScale;
        var innerRadius = 1.5f * UIScale;

        DrawGlowRing(handle, position, scale, outerRadius, outerGlow);
        DrawGlowRing(handle, position, scale, middleRadius, middleGlow);
        DrawGlowRing(handle, position, scale, innerRadius, innerGlow);
        handle.DrawString(
            _titleFont,
            position + new Vector2(0f, 2f * UIScale),
            _title,
            scale,
            Color.Black.WithAlpha(opacity * 0.80f));
        handle.DrawString(_titleFont, position, _title, scale, Color.White.WithAlpha(opacity));
    }

    private void DrawGlowRing(DrawingHandleScreen handle, Vector2 position, float scale, float radius, Color color)
    {
        foreach (var direction in GlowDirections)
        {
            handle.DrawString(_titleFont, position + direction * radius, _title, scale, color);
        }
    }

    private static float SmoothStep(float value)
    {
        return value * value * (3f - 2f * value);
    }

    private static float SmootherStep(float value)
    {
        return value * value * value * (value * (value * 6f - 15f) + 10f);
    }

    private static readonly Vector2[] GlowDirections =
    {
        new(-1f, 0f),
        new(1f, 0f),
        new(0f, -1f),
        new(0f, 1f),
        new(-0.7f, -0.7f),
        new(0.7f, -0.7f),
        new(-0.7f, 0.7f),
        new(0.7f, 0.7f)
    };

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        args.Handle();
    }
}
