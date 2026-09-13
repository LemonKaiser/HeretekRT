using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.DroneVision;

/// <summary>
/// Gives the controlling player a thermal silhouette overlay suitable for drone and remote-camera entities.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KDroneVisionComponent : Component;
