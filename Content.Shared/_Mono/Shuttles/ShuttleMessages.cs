using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Shuttles;

/// <summary>
/// Raised on the client when it wishes to travel somewhere via autopilot.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleConsoleAutopilotPositionMessage : BoundUserInterfaceMessage
{
    public MapCoordinates Coordinates;
    public Angle Angle;
}

/// <summary>
/// Requests an autopilot approach to the grid selected on the system map.
/// With auto docking enabled, the server resolves a valid pair of docking ports; otherwise it
/// creates a normal grid-relative autopilot anchor.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleConsoleAutopilotGridMessage : BoundUserInterfaceMessage
{
    public NetEntity TargetGrid;
}

/// <summary>
/// Requests docking with the nearest eligible grid.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleConsoleAutoDockRequestMessage : BoundUserInterfaceMessage;

/// <summary>
/// Changes the per-shuttle automatic docking setting.
/// </summary>
[Serializable, NetSerializable]
public sealed class ToggleAutoDockRequestMessage : BoundUserInterfaceMessage
{
    public bool Enabled;

    public ToggleAutoDockRequestMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

/// <summary>
/// Requests a controlled landing by author-defined celestial-body and landing-site identifiers.
/// No client map id, position or transform is trusted.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleConsolePlanetaryLandingRequestMessage : BoundUserInterfaceMessage
{
    public string CelestialBodyId;
    public string LandingSiteId;

    public ShuttleConsolePlanetaryLandingRequestMessage(string celestialBodyId, string landingSiteId)
    {
        CelestialBodyId = celestialBodyId;
        LandingSiteId = landingSiteId;
    }
}

/// <summary>
/// Requests launch from the current server-owned landing reservation.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleConsolePlanetaryLaunchRequestMessage : BoundUserInterfaceMessage;
