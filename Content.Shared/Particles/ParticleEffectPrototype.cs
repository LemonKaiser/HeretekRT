using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

namespace Content.Shared.Particles;

/// <summary>
/// Keyframe for a float-over-lifetime curve. Time is normalised 0–1.
/// </summary>
[DataRecord]
public partial record struct ParticleCurveKey
{
    [DataField(required: true)]
    public float Time { get; private set; }

    [DataField(required: true)]
    public float Value { get; private set; }
}

/// <summary>
/// Keyframe for a color-over-lifetime gradient. Time is normalised 0–1.
/// </summary>
[DataRecord]
public partial record struct ColorCurveKey
{
    /// <summary>
    /// Time along the particle's lifetime (0–1). 0 = birth, 1 = death.
    /// </summary>
    [DataField(required: true)]
    public float Time { get; private set; }

    /// <summary>
    /// Color at this point in the lifetime. Alpha channel is respected and multiplied with the alpha curve if present.
    /// </summary>
    [DataField(required: true)]
    public Color Color { get; private set; }
}

/// <summary>
/// Keyframe for a Vector2-over-lifetime curve. Time is normalised 0–1.
/// </summary>
[DataRecord]
public partial record struct Vector2CurveKey
{
    /// <summary>
    /// Time along the particle's lifetime (0–1). 0 = birth, 1 = death.
    /// </summary>
    [DataField(required: true)]
    public float Time { get; private set; }

    /// <summary>
    /// Vector2 value at this point in the lifetime. Interpretation depends on the context of the curve (force, velocity, etc).
    /// </summary>
    [DataField(required: true)]
    public Vector2 Value { get; private set; }
}

/// <summary>
/// Fires <see cref="Count"/> particles at <see cref="Time"/> after the emitter starts.
/// </summary>
[DataRecord]
public partial record struct ParticleBurstData()
{
    [DataField]
    public TimeSpan Time { get; private set; }

    [DataField]
    public int Count { get; private set; } = 10;
}

public enum EmissionShapeType : byte
{
    Point,      // All particles spawn at the emitter origin.
    CircleEdge, // Particles spawn randomly along the circumference of a circle with radius.
    CircleFill, // Particles spawn randomly within a circle with radius.
    Box,        // Particles spawn randomly within a rectangle.
}

/// <summary>
/// Relative importance of a cosmetic effect when the client has to reduce particle work.
/// </summary>
public enum ParticlePriority : byte
{
    Decorative,
    Normal,
    Important,
    Critical,
}

[DataRecord]
public partial record struct EmissionShapeData()
{
    /// <summary>
    /// Default to emitter's position.
    /// </summary>
    [DataField]
    public EmissionShapeType Type { get; private set; } = EmissionShapeType.Point;

    /// <summary>
    /// For circle shapes, radius of the circle. For box shape, half-extents of the rectangle.
    /// </summary>
    [DataField]
    public float Radius { get; private set; } = 0.5f;

    /// <summary>
    /// For box shape, half-extents of the rectangle. X = width/2, Y = height/2.
    /// </summary>
    [DataField]
    public Vector2 BoxExtents { get; private set; } = new(0.5f, 0.5f);
}

/// <summary>
/// Defines a reusable particle effect prototype.
/// </summary>
[Prototype]
public sealed partial class ParticleEffectPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<ParticleEffectPrototype>))]
    public string[]? Parents { get; private set; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    #region Visuals

    /// <summary>
    /// Texture drawn for each particle. Supports RSI states and plain texture paths.
    /// </summary>
    [DataField(required: true)]
    public SpriteSpecifier Sprite { get; private set; } = default!;

    /// <summary>
    /// Particle color at the start of its life. Ignored when <see cref="ColorOverLifetime"/> is set.
    /// </summary>
    [DataField]
    public Color StartColor { get; private set; } = Color.White;

    /// <summary>
    /// Particle color at the end of its life. Ignored when <see cref="ColorOverLifetime"/> is set.
    /// </summary>
    [DataField]
    public Color EndColor { get; private set; } = Color.Transparent;

    /// <summary>
    /// Multi-stop color gradient over lifetime. Overrides the <see cref="StartColor"/>/<see cref="EndColor"/> lerp when non-empty.
    /// Each key has a normalised time (0–1) and a color.
    /// </summary>
    [DataField]
    public List<ColorCurveKey> ColorOverLifetime { get; private set; } = new();

    /// <summary>
    /// Alpha curve over lifetime (0–1). Multiplied on top of the color's alpha channel.
    /// Leave empty to rely on alpha baked into the colors directly.
    /// </summary>
    [DataField]
    public List<ParticleCurveKey> AlphaOverLifetime { get; private set; } = new();

    /// <summary>
    /// Shader to apply when drawing particles.
    /// </summary>
    [DataField]
    public string? Shader { get; private set; }

    /// <summary>
    /// Draw order. Higher values render on top.
    /// </summary>
    [DataField]
    public int RenderLayer { get; private set; }

    /// <summary>
    /// Legacy compatibility flag. It is interpreted as <see cref="ParticlePriority.Critical"/>,
    /// but still never bypasses the user's Off setting.
    /// </summary>
    [DataField]
    public bool IgnoreQualitySettings { get; private set; }

    /// <summary>
    /// Priority used by the client budget scheduler. Higher-priority effects keep a larger
    /// share of the budget at reduced quality levels.
    /// </summary>
    [DataField]
    public ParticlePriority Priority { get; private set; } = ParticlePriority.Normal;

    /// <summary>
    /// Relative CPU and GPU cost of one live particle. One is the baseline; large, noisy,
    /// translucent particles should cost more than small sparks.
    /// </summary>
    [DataField]
    public float Cost { get; private set; } = 1f;

    /// <summary>
    /// Distance in world units at which this effect is no longer emitted or rendered.
    /// Zero disables distance culling for the effect.
    /// </summary>
    [DataField]
    public float MaxDistance { get; private set; }

    /// <summary>
    /// Conservative radius around the emitter used when evaluating <see cref="MaxDistance"/>.
    /// </summary>
    [DataField]
    public float BoundsRadius { get; private set; } = 1f;

    #endregion
    #region Size

    /// <summary>
    /// Base particle size in world units.
    /// </summary>
    [DataField]
    public float ParticleSize { get; private set; } = 0.2f;

    /// <summary>
    /// Per-particle size randomization.
    /// </summary>
    [DataField]
    public float SizeVariance { get; private set; }

    /// <summary>
    /// Size multiplier curve over lifetime. Leave empty for constant size.
    /// </summary>
    [DataField]
    public List<ParticleCurveKey> SizeOverLifetime { get; private set; } = new();

    /// <summary>
    /// Stretches particles along their velocity direction.
    /// 0 = no stretch. Higher values give a motion-blur style trail.
    /// </summary>
    [DataField]
    public float StretchFactor { get; private set; }

    #endregion
    #region Lifetime

    /// <summary>
    /// How long each particle lives.
    /// </summary>
    [DataField]
    public TimeSpan Lifetime { get; private set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Per-particle lifetime variance.
    /// </summary>
    [DataField]
    public TimeSpan LifetimeVariance { get; private set; } = TimeSpan.FromSeconds(0.2);

    #endregion
    #region Movement

    /// <summary>
    /// Base spawn speed in world units per second.
    /// </summary>
    [DataField]
    public float Speed { get; private set; } = 1f;

    /// <summary>
    /// Per-particle speed variance.
    /// </summary>
    [DataField]
    public float SpeedVariance { get; private set; } = 0.3f;

    /// <summary>
    /// Speed multiplier curve over lifetime. Leave empty for constant speed.
    /// </summary>
    [DataField]
    public List<ParticleCurveKey> SpeedOverLifetime { get; private set; } = new();

    /// <summary>
    /// Constant acceleration added to every particle each frame (map units/sec²).
    /// X = map-right, Y = map-up.
    /// </summary>
    [DataField]
    public Vector2 ConstantForce { get; private set; }

    /// <summary>
    /// Time-varying force added to velocity over the particle's lifetime.
    /// Sampled by normalized age (0–1), scaled by dt each frame.
    /// X = map-right, Y = map-up.
    /// </summary>
    [DataField]
    public List<Vector2CurveKey> ForceOverLifetime { get; private set; } = new();

    /// <summary>
    /// Positional nudge applied over lifetime, adds directly to position, not velocity.
    /// Useful for swirl or curl motion without altering the underlying velocity.
    /// X = map-right, Y = map-up.
    /// </summary>
    [DataField]
    public List<Vector2CurveKey> VelocityOverLifetime { get; private set; } = new();

    /// <summary>
    /// Downward drift in world units/sec, applied to position (not velocity).
    /// Negative values make particles float upward.
    /// </summary>
    [DataField]
    public float Gravity { get; private set; }

    /// <summary>
    /// Exponential drag coefficient applied to velocity. 0 = no drag.
    /// </summary>
    [DataField]
    public float Drag { get; private set; }

    /// <summary>
    /// Speed cap in world units/sec. 0 = no cap.
    /// </summary>
    [DataField]
    public float TerminalSpeed { get; private set; }

    /// <summary>
    /// Turbulence strength in world units/sec. 0 = off.
    /// Pair with <see cref="NoiseFrequency"/> to control jitter speed.
    /// </summary>
    [DataField]
    public float NoiseStrength { get; private set; }

    /// <summary>
    /// Animation speed of the noise field. Higher = faster turbulence.
    /// </summary>
    [DataField]
    public float NoiseFrequency { get; private set; } = 1f;

    /// <summary>
    /// Fraction of the emitter's velocity inherited by new particles (0–1).
    /// 1 = particles launch with the full emitter velocity, leaving trails.
    /// </summary>
    [DataField]
    public float InheritVelocity { get; private set; }

    #endregion
    #region Rotation

    /// <summary>
    /// Starting rotation in degrees.
    /// </summary>
    [DataField]
    public Angle StartRotation { get; private set; }

    /// <summary>
    /// Per-particle starting rotation variance in degrees. 180 = fully random.
    /// </summary>
    [DataField]
    public Angle StartRotationVariance { get; private set; }

    /// <summary>
    /// Spin speed in degrees per second.
    /// </summary>
    [DataField]
    public Angle RotationSpeed { get; private set; }

    /// <summary>
    /// Per-particle spin speed variance in degrees per second.
    /// </summary>
    [DataField]
    public Angle RotationSpeedVariance { get; private set; }

    #endregion
    #region Emission

    /// <summary>
    /// Particles emitted per second. Ignored only when <see cref="Burst"/> is true;
    /// timed <see cref="Bursts"/> may be combined with continuous emission.
    /// </summary>
    [DataField]
    public float EmissionRate { get; private set; } = 20f;

    /// <summary>
    /// Emission rate multiplier curve over the emitter's duration.
    /// When <see cref="Duration"/> > 0, t = age / duration. When duration is zero (infinite), t clamps to 1 after 1 second.
    /// </summary>
    [DataField]
    public List<ParticleCurveKey> EmissionOverTime { get; private set; } = new();

    /// <summary>
    /// Max live particles this emitter can have at once.
    /// Set this to roughly the highest number of particles you expect to see on screen at one time, not the total
    /// spawned over the effect's lifetime. Keep it low: it directly contributes to the global particle-cost budget.
    /// </summary>
    [DataField]
    public int MaxCount { get; private set; } = 50;

    /// <summary>
    /// When true, emits all <see cref="MaxCount"/> particles at once then stops immediately.
    /// </summary>
    [DataField]
    public bool Burst { get; private set; }

    /// <summary>
    /// Timed burst entries. Can be combined with continuous emission.
    /// </summary>
    [DataField]
    public List<ParticleBurstData> Bursts { get; private set; } = new();

    /// <summary>
    /// How long the emitter runs. 0 = forever.
    /// </summary>
    [DataField]
    public TimeSpan Duration { get; private set; }

    #endregion
    #region Space

    /// <summary>
    /// When true (default), particles simulate in world space and trail behind moving emitters.
    /// When false, particles move relative to the emitter origin.
    /// </summary>
    [DataField]
    public bool WorldSpace { get; private set; } = true;

    #endregion
    #region Shape

    [DataField]
    public EmissionShapeData Shape { get; private set; } = new();

    #endregion
    #region Angle

    /// <summary>
    /// Emission cone spread in degrees. 360 = omnidirectional.
    /// </summary>
    [DataField]
    public Angle SpreadAngle { get; private set; } = Angle.FromDegrees(360);

    /// <summary>
    /// Emission direction bias in degrees. 0 = map-up.
    /// </summary>
    [DataField]
    public Angle EmitAngle { get; private set; }

    #endregion
}
