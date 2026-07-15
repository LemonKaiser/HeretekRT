using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SectorMap.BUI;

/// <summary>
/// Server-authored data for the planetary layer of the shuttle NAV display. It contains no map ids
/// or world coordinates supplied by a client; requests are always made by stable prototype ids.
/// </summary>
[Serializable, NetSerializable]
public sealed class KoronusPlanetaryInterfaceState
{
    public bool Available;
    public bool CanLand;
    public bool CanLaunch;
    public bool NavigationSuppressed;
    public string? CurrentSystem;
    public string? LandedBody;
    public Vector2 StellarCenter;
    public List<KoronusCelestialBodyState> Bodies;
    public KoronusPlanetaryTransitState? Transit;

    public KoronusPlanetaryInterfaceState(
        bool available,
        bool canLand,
        bool canLaunch,
        bool navigationSuppressed,
        string? currentSystem,
        string? landedBody,
        Vector2 stellarCenter,
        List<KoronusCelestialBodyState> bodies,
        KoronusPlanetaryTransitState? transit)
    {
        Available = available;
        CanLand = canLand;
        CanLaunch = canLaunch;
        NavigationSuppressed = navigationSuppressed;
        CurrentSystem = currentSystem;
        LandedBody = landedBody;
        StellarCenter = stellarCenter;
        Bodies = bodies;
        Transit = transit;
    }

    public static KoronusPlanetaryInterfaceState Unavailable() => new(false, false, false, false, null, null, Vector2.Zero, new(), null);
}

[Serializable, NetSerializable]
public sealed class KoronusCelestialBodyState
{
    public string Id;
    public string Name;
    public string Description;
    public string Climate;
    public string BodyType;
    public float OrbitRadius;
    public float OrbitPhase;
    public float OrbitAngularSpeed;
    public float NavVisualRadius;
    public float LandingApproachRadius;
    public bool InLandingApproachRange;
    public List<KoronusLandingSiteState> LandingSites;

    public KoronusCelestialBodyState(
        string id,
        string name,
        string description,
        string climate,
        string bodyType,
        float orbitRadius,
        float orbitPhase,
        float orbitAngularSpeed,
        float navVisualRadius,
        float landingApproachRadius,
        bool inLandingApproachRange,
        List<KoronusLandingSiteState> landingSites)
    {
        Id = id;
        Name = name;
        Description = description;
        Climate = climate;
        BodyType = bodyType;
        OrbitRadius = orbitRadius;
        OrbitPhase = orbitPhase;
        OrbitAngularSpeed = orbitAngularSpeed;
        NavVisualRadius = navVisualRadius;
        LandingApproachRadius = landingApproachRadius;
        InLandingApproachRange = inLandingApproachRange;
        LandingSites = landingSites;
    }
}

/// <summary>
/// Presentation-only status of the server-owned atmospheric transition currently locking a shuttle.
/// </summary>
[Serializable, NetSerializable]
public sealed class KoronusPlanetaryTransitState
{
    public bool Landing;
    public float RemainingTime;

    public KoronusPlanetaryTransitState(bool landing, float remainingTime)
    {
        Landing = landing;
        RemainingTime = remainingTime;
    }
}

[Serializable, NetSerializable]
public sealed class KoronusLandingSiteState
{
    public string Id;
    public string Name;
    public Vector2 Size;
    public bool Enabled;
    public bool Occupied;
    public bool ReservedByCurrentShuttle;

    public KoronusLandingSiteState(
        string id,
        string name,
        Vector2 size,
        bool enabled,
        bool occupied,
        bool reservedByCurrentShuttle)
    {
        Id = id;
        Name = name;
        Size = size;
        Enabled = enabled;
        Occupied = occupied;
        ReservedByCurrentShuttle = reservedByCurrentShuttle;
    }
}
