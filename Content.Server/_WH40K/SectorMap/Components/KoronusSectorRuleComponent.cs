using System.Numerics;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Content.Server._WH40K.SectorMap.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Marks a round as a Koronus sector round and stores the runtime mapping of system ids to maps.
/// </summary>
[RegisterComponent, Access(typeof(KoronusSectorRuleSystem), typeof(KoronusPlanetarySystem), typeof(KoronusLandingPadSystem))]
public sealed partial class KoronusSectorRuleComponent : Component
{
    [DataField(required: true)]
    public ProtoId<KoronusSectorPrototype> Sector;

    [ViewVariables]
    public bool WaitingForStartMap = true;

    [ViewVariables]
    public Dictionary<string, MapId> SystemMaps = new();

    /// <summary>
    /// Runtime association of a surface prototype with its persistent preloaded map.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, MapId> SurfaceMaps = new();

    /// <summary>
    /// Systems whose runtime maps were replaced by a same-round user-data snapshot.
    /// The snapshot itself is owned by <see cref="KoronusSectorRuleSystem"/> and is never
    /// treated as persistent round data.
    /// </summary>
    [ViewVariables]
    public HashSet<string> ColdUnloadedSystems = new();

    /// <summary>
    /// Landing-site ownership keyed by celestial body and site id. Reservations never come from client data.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, EntityUid> LandingReservations = new();

    [ViewVariables]
    public long NextLandingSessionId = 1;

    /// <summary>
    /// Parking state survives destruction of the original shuttle grid. Individual fragments are
    /// kept here and additionally tagged so later GridSplit events can extend the same session.
    /// </summary>
    [ViewVariables]
    public Dictionary<long, KoronusLandingSession> LandingSessions = new();
}

public sealed class KoronusLandingSession
{
    public long Id;
    public string BodyId = string.Empty;
    public string SurfaceId = string.Empty;
    public string ReservationKey = string.Empty;
    public Vector2 LandingPosition;
    public Vector2 LandingSize;
    public EntityUid PrimaryGrid;
    public HashSet<EntityUid> Fragments = new();
    public HashSet<EntityUid> CoveredPadTiles = new();
    public TimeSpan LandedAt;
    public TimeSpan? Deadline;
    public bool Launching;
    public bool PadTilesCovered;
}
