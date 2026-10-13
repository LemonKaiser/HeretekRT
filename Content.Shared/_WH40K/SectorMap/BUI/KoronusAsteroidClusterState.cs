using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SectorMap.BUI;

/// <summary>Charted area only; transmitting it never loads the individual mining grids.</summary>
[Serializable, NetSerializable]
public sealed class KoronusAsteroidClusterState
{
    public int Number;
    public Vector2 Position;
    public float Radius;

    public KoronusAsteroidClusterState(int number, Vector2 position, float radius)
    {
        Number = number;
        Position = position;
        Radius = radius;
    }
}
