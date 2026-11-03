namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// Spawns entities inside corners.
/// </summary>
/// <remarks>
/// Dungeon data keys are:
/// - CornerClutter
/// </remarks>
public sealed partial class CornerClutterDunGen : IDunGenLayer
{
    [DataField]
    public float Chance = 0.50f;

    /// <summary>
    /// Keep entrances clear and require a free two-by-two area around the corner,
    /// so solid clutter can be walked around.
    /// </summary>
    [DataField]
    public bool PreservePassages;
}
