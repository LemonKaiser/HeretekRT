namespace Content.Shared._WH40K.ClassProgression;

/// <summary>
/// Closed opt-in list for the Soldier's Assault Jump. Only deliberately marked low barricades may be crossed;
/// walls, doors, grilles and every unmarked collision fixture still terminate the route.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kClassLowDashObstacleComponent : Component;
