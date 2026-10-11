using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._WH40K.Npcs;

/// <summary>
/// Handles periodic firing practice at a shooting range towards the NPC's facing orientation.
/// Pauses when the NPC is sleeping or when no living players are within range.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kNpcRangePracticeComponent : Component
{
    /// <summary>
    /// Interval in seconds between practice shots when active.
    /// </summary>
    [DataField("interval")]
    public float Interval = 60f;

    /// <summary>
    /// Accumulator tracking elapsed active time until the next shot.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float Accumulator;

    /// <summary>
    /// Maximum distance to scan for nearby living players.
    /// </summary>
    [DataField("playerCheckRange")]
    public float PlayerCheckRange = 12f;

    /// <summary>
    /// Distance in tiles ahead of the NPC along its facing angle to place the target coordinate.
    /// </summary>
    [DataField("targetDistance")]
    public float TargetDistance = 6f;
}
