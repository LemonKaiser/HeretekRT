using Robust.Shared.Maths;

namespace Content.Shared.Parallax.Biomes;

/// <summary>
/// Optional hard limit for procedural biome output on a grid.
/// Existing authored tiles are untouched; new biome tiles, entities, decals and markers are
/// generated only inside this rectangle.
/// </summary>
[RegisterComponent]
public sealed partial class BiomeGenerationBoundsComponent : Component
{
    /// <summary>
    /// Tile-space rectangle with an exclusive upper edge.
    /// </summary>
    public Box2 Bounds;
}
