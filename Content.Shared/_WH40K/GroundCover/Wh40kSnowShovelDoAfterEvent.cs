using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.GroundCover;

/// <summary>Raised on a shovel when clearing snow off a tile ends.</summary>
[Serializable, NetSerializable]
public sealed partial class Wh40kSnowShovelDoAfterEvent : DoAfterEvent
{
    public NetEntity Grid;

    public Vector2i Tile;

    public Wh40kSnowShovelDoAfterEvent(NetEntity grid, Vector2i tile)
    {
        Grid = grid;
        Tile = tile;
    }

    public override DoAfterEvent Clone()
    {
        return new Wh40kSnowShovelDoAfterEvent(Grid, Tile);
    }

    public override bool IsDuplicate(DoAfterEvent other)
    {
        return other is Wh40kSnowShovelDoAfterEvent dig && Grid == dig.Grid && Tile == dig.Tile;
    }
}
