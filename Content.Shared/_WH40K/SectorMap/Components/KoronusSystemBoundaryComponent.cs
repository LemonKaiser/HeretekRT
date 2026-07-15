using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>
/// Networked boundary data for a loaded Koronus system map.
/// The server enforces it while clients use it for world and shuttle-map warnings.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KoronusSystemBoundaryComponent : Component
{
    [DataField, AutoNetworkedField]
    public Vector2 Origin;

    [DataField, AutoNetworkedField]
    public float Radius;

    [DataField, AutoNetworkedField]
    public float WarningFraction = 0.9f;

    [DataField, AutoNetworkedField]
    public float CleanupDelay = 10f;

    [DataField, AutoNetworkedField]
    public float WarningAnnouncementCooldown = 600f;
}
