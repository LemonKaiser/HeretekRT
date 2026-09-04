using System.Numerics;
using Content.Shared.Particles;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client.Particles;

/// <summary>
/// A running particle emitter and its live particle pool.
/// Created in <see cref="ParticleSystem"/>.
/// </summary>
public sealed class ActiveEmitter
{
    public ParticleEffectPrototype Proto = default!;

    /// <summary>
    /// Current world-space origin of the emitter.
    /// </summary>
    public MapCoordinates MapCoords;

    /// <summary>
    /// Entity this emitter follows (if any).
    /// </summary>
    public EntityUid? AttachedEntity;

    /// <summary>
    /// Time elapsed since this emitter was created, in seconds.
    /// </summary>
    public float Age;

    /// <summary>
    /// Emission accumulator for sub-tick emission rates.
    /// </summary>
    public float EmitAccum;

    /// <summary>
    /// True once the emitter stops producing new particles. Existing particles live out their lifetimes.
    /// </summary>
    public bool Exhausted;

    /// <summary>
    /// Unique client-side handle for addressing this emitter by ID.
    /// Prefer holding the <see cref="ActiveEmitter"/> reference directly when possible, please.
    /// </summary>
    public uint Handle;

    /// <summary>
    /// Color tint multiplied on top of every particle's computed color.
    /// </summary>
    public Color? ColorOverride;

    /// <summary>
    /// Intensity multiplier for emission rate and particle size. 1.0 = normal.
    /// </summary>
    public float Intensity = 1f;

    /// <summary>
    /// World-space velocity added to every particle on creation.
    /// </summary>
    public Vector2 InitialVelocity;

    /// <summary>
    /// Quality and distance multiplier resolved once per frame by <see cref="ParticleSystem"/>.
    /// </summary>
    public float LodMultiplier = 1f;

    /// <summary>
    /// Squared distance from the local eye to the emitter, used to order the budget scheduler.
    /// </summary>
    public float DistanceSquared;

    /// <summary>
    /// Cost of one live particle from this emitter.
    /// </summary>
    public float ParticleCost = 1f;

    internal CompiledParticleEffect Runtime = default!;
    internal ParticleRandom Random = default!;

    #region Velocity tracking

    public Vector2 PreviousPosition;
    public Vector2 EmitterVelocity;
    public bool VelocityInitialized;

    #endregion

    /// <summary>
    /// Map-space emission angle in radians, resolved from the immutable prototype on creation.
    /// </summary>
    public float EffectiveEmitAngle;

    #region Timed bursts

    /// <summary>
    /// Tracks which <see cref="ParticleEffectPrototype.Bursts"/> entries have already fired.
    /// </summary>
    public readonly List<bool> FiredBursts = new();

    #endregion

    #region Animation

    /// <summary>
    /// Resolved RSI frames. Populated on creation.
    /// Single-frame sprites have one entry and empty Delays.
    /// </summary>
    public Texture[] Frames = Array.Empty<Texture>();

    /// <summary>
    /// Frame delays when an RSI defines animation.
    /// </summary>
    public float[] Delays = Array.Empty<float>();

    /// <summary>
    /// Shared animation clock. Each particle adds its own <see cref="ParticleData.AnimationPhase"/>
    /// when selecting a frame.
    /// </summary>
    public float AnimationTime;

    /// <summary>
    /// Total duration of one RSI animation cycle, in seconds. Zero means a static sprite.
    /// </summary>
    public float AnimationDuration;

    #endregion

    #region Particles

    /// <summary>
    /// Dense set of live particles. Death uses swap-remove, so the list has no dead slots.
    /// </summary>
    public readonly List<ParticleData> Particles = new();

    #endregion
}
