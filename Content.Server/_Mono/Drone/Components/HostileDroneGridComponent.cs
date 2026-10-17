namespace Content.Server._Mono.Drone.Components;

/// <summary>
/// Marks a grid as being a drone.
/// </summary>
[RegisterComponent]
public sealed partial class HostileDroneGridComponent : Component
{
    /// <summary>
    /// Whether this drone grid has been defeated/neutralized and turned into wreckage.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Defeated;

    /// <summary>
    /// Whether initial equipment presence has been recorded for this drone grid.
    /// </summary>
    [DataField]
    public bool Initialized;

    [DataField]
    public bool HadAi;

    [DataField]
    public bool HadThrusters;

    [DataField]
    public bool HadWeapons;

    /// <summary>
    /// Accumulator for periodic status evaluations.
    /// </summary>
    public float CheckAccumulator;

    /// <summary>
    /// Interval in seconds between status evaluations.
    /// </summary>
    [DataField]
    public float CheckInterval = 1.0f;
}