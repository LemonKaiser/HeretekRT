namespace Content.Shared._WH40K.Visuals.WorldEffects;

/// <summary>
/// Configures the client-side glow, bloom and light-haze pipeline for a light source.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KLightEffectsComponent : Component
{
    [DataField]
    public WH40KLightEffectProfile Profile = WH40KLightEffectProfile.Fixture;

    [DataField]
    public WH40KLightEmitterLayer EmitterLayer = WH40KLightEmitterLayer.PoweredGlow;

    [DataField]
    public string? CustomLayer;

    [DataField]
    public WH40KLightHazeMode Haze = WH40KLightHazeMode.Auto;

    [DataField]
    public float? GlowStrength;

    [DataField]
    public float? BloomStrength;

    [DataField]
    public float? HazeStrength;

    [DataField]
    public float? EffectScale;

    [DataField]
    public float? HazeLengthScale;

    [DataField]
    public float? HazeWidthScale;

    [DataField]
    public float? HazeRadiusScale;

    [DataField]
    public float? MinLightRadius;
}

public enum WH40KLightEffectProfile : byte
{
    Fixture,
    Candle,
    PortableLamp,
    Floodlight,
}

public enum WH40KLightEmitterLayer : byte
{
    PoweredGlow,
    ToggleableLight,
    Fire,
    TopmostUnshaded,
    Custom,
}

public enum WH40KLightHazeMode : byte
{
    Auto,
    None,
    Directional,
    RadialMounted,
    RadialOmnidirectional,
}
