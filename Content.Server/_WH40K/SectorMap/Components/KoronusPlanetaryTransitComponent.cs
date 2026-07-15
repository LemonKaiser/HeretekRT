using System.Numerics;
using Content.Server._WH40K.SectorMap.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Server-only state of a controlled atmospheric descent or ascent. The middle phase is performed
/// on a dedicated atmospheric map, separate from the normal FTL map and its drive/cooldown rules.
/// </summary>
[RegisterComponent, Access(typeof(KoronusPlanetarySystem))]
public sealed partial class KoronusPlanetaryTransitComponent : Component
{
    public bool Landing;
    public MapId SourceMap;
    public Vector2 SourceOrigin;
    public Angle SourceAngle;
    public MapId TargetMap;
    public Vector2 TargetOrigin;
    /// <summary>
    /// Body whose current orbital position must be used when an ascent completes. Keeping the id
    /// rather than a cached position prevents a moving planet from leaving the shuttle behind.
    /// </summary>
    public string? LaunchBodyId;
    public MapId TransitMap;
    public Vector2 TransitOrigin;
    public TimeSpan TravelAt;
    public TimeSpan ArrivalAt;
    public TimeSpan CompleteAt;
    public string ReservationKey = string.Empty;
    public bool AddedPreventPilot;
    public bool EnteredTransitSpace;
    public EntityUid? TravelStream;

    /// <summary>
    /// Mobs which entered atmospheric transit aboard this shuttle. If one stops belonging to the
    /// shuttle grid while it is on the technical transit map, it must fall to the planet instead of
    /// remaining stranded there.
    /// </summary>
    public readonly HashSet<EntityUid> TransitMobs = new();
}
