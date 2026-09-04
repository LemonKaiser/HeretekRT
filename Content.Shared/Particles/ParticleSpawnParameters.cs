using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared.Particles;

/// <summary>
/// Per-instance parameters for a cosmetic particle emitter.
/// They intentionally describe presentation only and never carry gameplay state.
/// </summary>
[Serializable, NetSerializable]
public sealed record ParticleSpawnParameters(
    Color? Color = null,
    Angle? EmitAngle = null,
    Vector2? Velocity = null,
    float Intensity = 1f,
    int? Seed = null)
{
    public static readonly ParticleSpawnParameters Default = new();
}
