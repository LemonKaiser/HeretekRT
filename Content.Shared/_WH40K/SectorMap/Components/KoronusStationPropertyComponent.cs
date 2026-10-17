using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>Applied to authored facility contents once, never to items merely dropped on its grid.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KoronusStationPropertyComponent : Component
{
    [DataField, AutoNetworkedField]
    public KoronusStationPropertyPolicy Policy = KoronusStationPropertyPolicy.Display;

    [DataField, AutoNetworkedField]
    public EntityUid? HomeGrid;
}

[Serializable, NetSerializable]
public enum KoronusStationPropertyPolicy : byte
{
    Display,
    LocalUse,
    Free,
}

/// <summary>Authored public storage. This does not make later contents station property.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KoronusStationPropertyStorageComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? HomeGrid;
}
