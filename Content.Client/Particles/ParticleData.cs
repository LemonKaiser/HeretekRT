using System.Numerics;

namespace Content.Client.Particles;

/// <summary>
/// Runtime state for one live particle. It is a value type deliberately kept in a dense list:
/// a dead particle is swap-removed instead of surviving as a tombstone in a managed object pool.
/// </summary>
public struct ParticleData
{
    /// <summary>
    /// Particle position in map coordinates for world-space effects, or relative to the emitter
    /// in map axes for local-space effects.
    /// </summary>
    public Vector2 Position;

    /// <summary>
    /// Current velocity in map units per second.
    /// </summary>
    public Vector2 Velocity;

    /// <summary>
    /// Elapsed lifetime in seconds.
    /// </summary>
    public float Age;

    /// <summary>
    /// Lifetime and its reciprocal are stored as floats to avoid repeated TimeSpan and division work.
    /// </summary>
    public float Lifetime;
    public float InverseLifetime;

    public float SpawnSpeed;
    public float SpawnIntensity;
    public float Rotation;
    public float RotationSpeed;
    public float SizeMultiplier;

    /// <summary>
    /// Stable random offset into the emitter RSI animation, in seconds.
    /// This keeps otherwise identical smoke particles from changing frame in lockstep.
    /// </summary>
    public float AnimationPhase;

    public Vector2 NoiseOffset;

    public readonly float AgeRatio => Math.Clamp(Age * InverseLifetime, 0f, 1f);
}
