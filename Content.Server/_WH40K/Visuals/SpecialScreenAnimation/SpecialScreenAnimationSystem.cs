using Content.Shared._WH40K.Visuals.SpecialScreenAnimation;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Visuals.SpecialScreenAnimation;

public sealed class SpecialScreenAnimationSystem : SharedSpecialScreenAnimationSystem
{
    private const float MinimumDuration = 0.1f;
    private const float MaximumDuration = 15f;

    public override void PlayForPlayer(
        SpriteSpecifier sprite,
        EntityUid player,
        SpecialScreenAnimationData? animation = null,
        string? text = null)
    {
        if (!TryCreateAnimation(sprite, animation, text, out var result))
            return;

        RaiseNetworkEvent(new PlaySpecialScreenAnimationEvent(result), player);
    }

    public override void PlayForFilter(
        SpriteSpecifier sprite,
        Filter filter,
        SpecialScreenAnimationData? animation = null,
        string? text = null)
    {
        if (!TryCreateAnimation(sprite, animation, text, out var result))
            return;

        RaiseNetworkEvent(new PlaySpecialScreenAnimationEvent(result), filter);
    }

    private static bool TryCreateAnimation(
        SpriteSpecifier sprite,
        SpecialScreenAnimationData? animation,
        string? text,
        out SpecialScreenAnimationData result)
    {
        result = (animation ?? new SpecialScreenAnimationData()).Copy();
        result.Sprite = sprite;
        result.TotalDuration = Math.Clamp(result.TotalDuration, MinimumDuration, MaximumDuration);
        result.FadeInDuration = Math.Clamp(result.FadeInDuration, 0f, result.TotalDuration * 0.49f);
        result.FadeOutDuration = Math.Clamp(result.FadeOutDuration, 0f, result.TotalDuration - result.FadeInDuration - 0.01f);
        result.Scale = Math.Clamp(result.Scale, 0.05f, 20f);
        result.MaxOpacity = Math.Clamp(result.MaxOpacity, 0f, 1f);
        result.TextFontSize = Math.Clamp(result.TextFontSize, 8, 96);

        if (text != null)
            result.Text = text;

        return true;
    }
}
