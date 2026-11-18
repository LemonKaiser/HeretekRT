using System.Numerics;
using Content.Shared.Particles;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Weapons;

[Serializable, NetSerializable]
public sealed class WH40KWallMarkEvent(NetEntity wall, Vector2 offset, ParticleSurfaceMaterial material, Angle rotation)
    : EntityEventArgs
{
    public readonly NetEntity Wall = wall;
    public readonly Vector2 Offset = offset;
    public readonly ParticleSurfaceMaterial Material = material;
    public readonly Angle Rotation = rotation;
}
