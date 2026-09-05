using System.Numerics;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.Visuals.SpecialScreenAnimation;

/// <summary>
/// Serializable presentation data for a short screen-space animation.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class SpecialScreenAnimationData
{
    [DataField]
    public float TotalDuration = 2.8f;

    [DataField]
    public float Scale = 1f;

    [DataField]
    public float MaxOpacity = 0.6f;

    [DataField]
    public float FadeInDuration = 0.35f;

    [DataField]
    public float FadeOutDuration = 0.35f;

    [DataField]
    public Vector2 StartPosition;

    [DataField]
    public Vector2 EndPosition;

    [DataField]
    public string? Text;

    [DataField]
    public Color TextColor = Color.White;

    [DataField]
    public Vector2 TextPosition;

    [DataField]
    public string TextFontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";

    [DataField]
    public int TextFontSize = 26;

    [DataField(required: true)]
    public SpriteSpecifier? Sprite;

    public SpecialScreenAnimationData Copy()
    {
        return new SpecialScreenAnimationData
        {
            TotalDuration = TotalDuration,
            Scale = Scale,
            MaxOpacity = MaxOpacity,
            FadeInDuration = FadeInDuration,
            FadeOutDuration = FadeOutDuration,
            StartPosition = StartPosition,
            EndPosition = EndPosition,
            Text = Text,
            TextColor = TextColor,
            TextPosition = TextPosition,
            TextFontPath = TextFontPath,
            TextFontSize = TextFontSize,
            Sprite = Sprite,
        };
    }
}
