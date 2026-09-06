using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Content.Shared._NF.Shuttles.Events; // Frontier - InertiaDampeningMode access
using Content.Shared._WH40K.SectorMap.BUI;

namespace Content.Shared.Shuttles.BUIStates;

[Serializable, NetSerializable]
public sealed class NavInterfaceState
{
    public float MaxRange;

    /// <summary>
    /// The relevant coordinates to base the radar around.
    /// </summary>
    public NetCoordinates? Coordinates;

    /// <summary>
    /// The relevant rotation to rotate the angle around.
    /// </summary>
    public Angle? Angle;

    public Dictionary<NetEntity, List<DockingPortState>> Docks;

    /// <summary>
    /// Custom display names for network port buttons.
    /// Key is the port ID, value is the display name.
    /// </summary>
    public Dictionary<string, string> NetworkPortNames = new();

    // Frontier fields
    /// <summary>
    /// Frontier - the state of the shuttle's inertial dampeners
    /// </summary>
    public InertiaDampeningMode DampeningMode;

    /// <summary>
    /// Frontier: settable maximum IFF range
    /// </summary>
    public float? MaxIffRange = null;

    /// <summary>
    /// Frontier: settable coordinate visibility
    /// </summary>
    public bool HideCoords = false;
    // End Frontier fields

    /// <summary>
    /// Read-only celestial layer for every consumer of the shared NAV control.
    /// Keeping it in the common state prevents radar, fire-control and drone consoles from
    /// silently losing planets while the shuttle console still displays them.
    /// </summary>
    public KoronusPlanetaryInterfaceState PlanetaryState;

    public NavInterfaceState(
        float maxRange,
        NetCoordinates? coordinates,
        Angle? angle,
        Dictionary<NetEntity, List<DockingPortState>> docks,
        InertiaDampeningMode dampeningMode, // Frontier: add dampeningMode
        Dictionary<string, string>? networkPortNames = null,
        KoronusPlanetaryInterfaceState? planetaryState = null)
    {
        MaxRange = maxRange;
        Coordinates = coordinates;
        Angle = angle;
        Docks = docks;
        DampeningMode = dampeningMode; // Frontier
        NetworkPortNames = networkPortNames ?? new Dictionary<string, string>();
        PlanetaryState = planetaryState ?? KoronusPlanetaryInterfaceState.Unavailable();
    }
}

[Serializable, NetSerializable]
public enum RadarConsoleUiKey : byte
{
    Key
}
