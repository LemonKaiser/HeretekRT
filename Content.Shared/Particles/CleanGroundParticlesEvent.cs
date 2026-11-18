using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Particles;

/// <summary>
/// Clears cosmetic particle marks on a washed tile.
/// </summary>
[Serializable, NetSerializable]
public sealed class CleanGroundParticlesEvent(NetCoordinates coordinates) : EntityEventArgs
{
    public readonly NetCoordinates Coordinates = coordinates;
}
