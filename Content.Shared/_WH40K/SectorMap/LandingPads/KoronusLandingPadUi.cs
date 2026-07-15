using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SectorMap.LandingPads;

[Serializable, NetSerializable]
public enum KoronusLandingPadUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class KoronusLandingPadBoundUserInterfaceState : BoundUserInterfaceState
{
    public string Name;
    public int ParkingTime;
    public bool PublicAccess;
    public bool Enabled;
    public bool Locked;
    public bool Powered;
    public bool PrimaryConsole;
    public bool Occupied;
    public string? ShuttleName;
    public string? ShuttleOwner;
    public float? RemainingParkingTime;

    public KoronusLandingPadBoundUserInterfaceState(
        string name,
        int parkingTime,
        bool publicAccess,
        bool enabled,
        bool locked,
        bool powered,
        bool primaryConsole,
        bool occupied,
        string? shuttleName,
        string? shuttleOwner,
        float? remainingParkingTime)
    {
        Name = name;
        ParkingTime = parkingTime;
        PublicAccess = publicAccess;
        Enabled = enabled;
        Locked = locked;
        Powered = powered;
        PrimaryConsole = primaryConsole;
        Occupied = occupied;
        ShuttleName = shuttleName;
        ShuttleOwner = shuttleOwner;
        RemainingParkingTime = remainingParkingTime;
    }
}

[Serializable, NetSerializable]
public sealed class KoronusLandingPadConfigureMessage : BoundUserInterfaceMessage
{
    public string Name;
    public int ParkingTime;
    public bool PublicAccess;
    public bool Enabled;

    public KoronusLandingPadConfigureMessage(string name, int parkingTime, bool publicAccess, bool enabled)
    {
        Name = name;
        ParkingTime = parkingTime;
        PublicAccess = publicAccess;
        Enabled = enabled;
    }
}
