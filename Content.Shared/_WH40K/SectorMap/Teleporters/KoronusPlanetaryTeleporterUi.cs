using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SectorMap.Teleporters;

[Serializable, NetSerializable]
public enum KoronusPlanetaryTeleporterUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class KoronusPlanetaryTeleporterState : BoundUserInterfaceState
{
    public bool PlanetSide;
    public bool PublicAccess;
    public bool Locked;
    public bool Powered;
    public bool Active;

    public KoronusPlanetaryTeleporterState(
        bool planetSide,
        bool publicAccess,
        bool locked,
        bool powered,
        bool active)
    {
        PlanetSide = planetSide;
        PublicAccess = publicAccess;
        Locked = locked;
        Powered = powered;
        Active = active;
    }
}

[Serializable, NetSerializable]
public sealed class KoronusPlanetaryTeleporterTargetsMessage : BoundUserInterfaceMessage
{
    public List<KoronusPlanetaryTeleporterTargetState> Targets;

    public KoronusPlanetaryTeleporterTargetsMessage(List<KoronusPlanetaryTeleporterTargetState> targets)
    {
        Targets = targets;
    }
}

[Serializable, NetSerializable]
public sealed class KoronusPlanetaryTeleporterTargetState
{
    public string Id;
    public string Name;
    public bool Available;
    public bool Selected;

    public KoronusPlanetaryTeleporterTargetState(string id, string name, bool available, bool selected)
    {
        Id = id;
        Name = name;
        Available = available;
        Selected = selected;
    }
}

[Serializable, NetSerializable]
public sealed class KoronusPlanetaryTeleporterSelectMessage : BoundUserInterfaceMessage
{
    public string TargetId;

    public KoronusPlanetaryTeleporterSelectMessage(string targetId)
    {
        TargetId = targetId;
    }
}

[Serializable, NetSerializable]
public sealed class KoronusPlanetaryTeleporterAccessMessage : BoundUserInterfaceMessage
{
    public bool PublicAccess;

    public KoronusPlanetaryTeleporterAccessMessage(bool publicAccess)
    {
        PublicAccess = publicAccess;
    }
}
