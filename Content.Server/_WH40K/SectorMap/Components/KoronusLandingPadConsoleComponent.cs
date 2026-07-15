using Content.Server._WH40K.SectorMap.Systems;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Configuration and status terminal for the cardinally adjacent landing-pad tile group.
/// </summary>
[RegisterComponent, Access(typeof(KoronusLandingPadSystem))]
public sealed partial class KoronusLandingPadConsoleComponent : Component
{
    public const int MaxNameLength = 12;
    public const int MaxParkingTime = 3600;

    /// <summary>Optional map-authored stable id. Player-built consoles use their runtime entity id.</summary>
    [DataField]
    public string? PadId;

    [DataField]
    public string PadName = "Площадка";

    /// <summary>Zero disables forced departure; otherwise the accepted range is 1..3600 seconds.</summary>
    [DataField]
    public int ParkingTime;

    [DataField]
    public bool PublicAccess = true;

    [DataField]
    public bool Enabled = true;

    /// <summary>Map-authored consoles may expose status while rejecting every configuration change.</summary>
    [DataField]
    public bool Locked;
}
