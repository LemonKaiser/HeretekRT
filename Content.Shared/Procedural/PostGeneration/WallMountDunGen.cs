namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// Spawns on the boundary tiles of rooms.
/// </summary>
public sealed partial class WallMountDunGen : IDunGenLayer
{
    /// <summary>
    /// Chance per free tile to spawn a wallmount.
    /// </summary>
    [DataField]
    public double Prob = 0.1;

    /// <summary>
    /// Mount on the interior tile beside an existing straight wall and face the wall.
    /// Use for fixtures whose sprite is offset one tile towards their mounting wall.
    /// Run this after boundary walls have been placed.
    /// </summary>
    [DataField]
    public bool MountInside;

    /// <summary>
    /// Minimum tile distance between fixtures facing the same direction.
    /// </summary>
    [DataField]
    public int MinSpacing;
}
