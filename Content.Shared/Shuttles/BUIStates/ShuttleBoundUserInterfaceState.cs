using Content.Shared.Shuttles.UI.MapObjects;
using Content.Shared._WH40K.SectorMap.BUI;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.BUIStates;

[Serializable, NetSerializable]
public sealed class ShuttleBoundUserInterfaceState : BoundUserInterfaceState
{
    public NavInterfaceState NavState;
    public ShuttleMapInterfaceState MapState;
    public DockingInterfaceState DockState;
    public KoronusSectorInterfaceState SectorState;
    public KoronusPlanetaryInterfaceState PlanetaryState;

    public ShuttleBoundUserInterfaceState(
        NavInterfaceState navState,
        ShuttleMapInterfaceState mapState,
        DockingInterfaceState dockState,
        KoronusSectorInterfaceState sectorState,
        KoronusPlanetaryInterfaceState planetaryState)
    {
        NavState = navState;
        MapState = mapState;
        DockState = dockState;
        SectorState = sectorState;
        PlanetaryState = planetaryState;
    }
}
