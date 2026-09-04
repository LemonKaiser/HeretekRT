using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Particles;

/// <summary>
/// Requests a cosmetic particle burst for clients that can currently observe the source coordinates.
/// </summary>
[Serializable, NetSerializable]
public sealed class SpawnParticlesEvent(
    NetCoordinates coordinates,
    NetEntity? attachedEntity,
    ProtoId<ParticleEffectPrototype> effect,
    int count,
    ParticleSpawnParameters parameters) : EntityEventArgs
{
    public readonly NetCoordinates Coordinates = coordinates;
    public readonly NetEntity? AttachedEntity = attachedEntity;
    public readonly ProtoId<ParticleEffectPrototype> Effect = effect;
    public readonly int Count = count;
    public readonly ParticleSpawnParameters Parameters = parameters;
}
