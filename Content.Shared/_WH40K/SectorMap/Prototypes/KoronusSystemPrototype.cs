using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.SectorMap.Prototypes;

/// <summary>
/// One fixed Koronus Expanse system. Its presentation and runtime map are configured entirely in prototypes.
/// </summary>
[Prototype("koronusSystem")]
public sealed partial class KoronusSystemPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<KoronusSectorPrototype> Sector;

    [DataField(required: true)]
    public string DisplayName = string.Empty;

    /// <summary>
    /// Normalized position on the sector map texture, in the 0..1 range.
    /// </summary>
    [DataField]
    public Vector2 UiPosition;

    /// <summary>
    /// Authored world-space centre around which inter-system warp arrivals are placed.
    /// </summary>
    [DataField]
    public Vector2 ArrivalPosition = Vector2.Zero;

    /// <summary>
    /// Selects whether this system is represented as ordinary local space or as a stellar navigation scheme.
    /// </summary>
    [DataField]
    public KoronusSpaceMode SpaceMode = KoronusSpaceMode.Standard;

    /// <summary>
    /// Authored position of the star in a planetary system. It is the source of boundaries, orbit display
    /// and safe orbital arrivals; an optional initial grid must never redefine it.
    /// </summary>
    [DataField]
    public Vector2 StellarCenter = Vector2.Zero;

    /// <summary>
    /// Radius around <see cref="ArrivalPosition"/> at which an inter-system warp arrival is placed.
    /// </summary>
    [DataField]
    public float ArrivalDistance = 1000f;

    /// <summary>
    /// Optional authored grid loaded into the orbital map. Planetary systems may deliberately start empty.
    /// </summary>
    [DataField]
    public ResPath? InitialGridPath;

    /// <summary>
    /// When positive, places the loaded initial grid at this exact distance from the system's
    /// navigation centre under a new random angle each round. Zero preserves the map-authored position.
    /// </summary>
    [DataField]
    public float InitialGridSpawnDistance;

    /// <summary>
    /// Optional in-system name for the initial facility grid. This is separate from the stellar
    /// system name used by the sector map.
    /// </summary>
    [DataField]
    public string? InitialGridDisplayName;

    /// <summary>
    /// Optional profile attached to every grid loaded as this system's authored facility.
    /// Unlike a static system circle, this follows the facility when it is moved at round start.
    /// </summary>
    [DataField]
    public ProtoId<KoronusSafetyProfilePrototype>? InitialGridSafetyProfile;

    [DataField]
    public float InitialGridSafetyRadius;

    /// <summary>
    /// Optional rules attached directly to the authored facility grid. Unlike a circular safety
    /// zone, these rules never apply to open space or another grid on the same system map.
    /// </summary>
    [DataField]
    public ProtoId<KoronusSafetyProfilePrototype>? InitialGridLocalSafetyProfile;

    /// <summary>
    /// Adds the explicit infrastructure protection components to authored facility grids.
    /// Procedural terrain and asteroid grids never receive these components automatically.
    /// </summary>
    [DataField]
    public bool ProtectInitialGrid;

    /// <summary>
    /// Additional authored facility grids loaded alongside the initial grid on this system map.
    /// Each facility owns its placement, name and safeguards, so one system can host several bases.
    /// </summary>
    [DataField]
    public List<KoronusAdditionalGridDefinition> AdditionalGrids = new();

    [DataField]
    public bool Enabled;

    /// <summary>
    /// Allows only data-only Rogue Trader sector activities to use this system as a marker target.
    /// The protected Footfall system intentionally leaves this disabled.
    /// </summary>
    [DataField]
    public bool ActivityEligible;

    /// <summary>
    /// Optional finite asteroid field for an external, activity-eligible system. It is generated
    /// once when the system map is first created and is never used for Footfall.
    /// </summary>
    [DataField]
    public KoronusAsteroidFieldDefinition? AsteroidField;

    [DataField]
    public float BoundaryRadius = 20000f;

    [DataField]
    public float WarningFraction = 0.9f;

    [DataField]
    public float CleanupDelay = 10f;

    [DataField]
    public float WarningAnnouncementCooldown = 600f;

    [DataField]
    public bool PauseWhenEmpty = true;

    [DataField]
    public bool UnpauseOnAdminGhost = true;

    [DataField]
    public float RepauseDelay = 10f;

    [DataField]
    public bool HoldAwakeOnIncomingSectorJump = true;

    /// <summary>
    /// Allows an empty, paused remote system to be serialized and removed from runtime memory.
    /// The starting system is never cold-unloaded, even when this is enabled by mistake.
    /// </summary>
    [DataField]
    public bool AllowColdUnload;

    /// <summary>
    /// Minimum time an empty system remains paused before its snapshot is written and the map is removed.
    /// A non-positive value disables cold-unload for this system.
    /// </summary>
    [DataField]
    public float ColdUnloadDelay = 120f;

    /// <summary>
    /// Restrictions applied everywhere on this system map.
    /// </summary>
    [DataField]
    public ProtoId<KoronusSafetyProfilePrototype>? SafetyProfile;

    /// <summary>
    /// Additional circular safety areas, normally used around stationary facilities.
    /// </summary>
    [DataField]
    public List<KoronusSafetyZoneDefinition> SafetyZones = new();

    /// <summary>
    /// Returns the one authoritative centre used by system-scale gameplay.
    /// </summary>
    public Vector2 NavigationCenter => SpaceMode == KoronusSpaceMode.Planetary
        ? StellarCenter
        : ArrivalPosition;
}

public enum KoronusSpaceMode : byte
{
    Standard,
    Planetary,
}

/// <summary>
/// Finite ring of pre-existing mining asteroids. The definition intentionally exposes no map,
/// entity or reward input: the server selects from its closed audited pool.
/// </summary>
[DataDefinition]
public sealed partial class KoronusAsteroidFieldDefinition
{
    [DataField(required: true)]
    public int Count;

    [DataField]
    public float InnerRadius = 8000f;

    [DataField]
    public float OuterRadius = 10000f;
}

/// <summary>
/// One supplementary authored facility on a Koronus system map.
/// </summary>
[DataDefinition]
public sealed partial class KoronusAdditionalGridDefinition
{
    [DataField(required: true)]
    public ResPath MapPath;

    [DataField]
    public float SpawnDistance;

    [DataField]
    public string? DisplayName;

    [DataField]
    public ProtoId<KoronusSafetyProfilePrototype>? SafetyProfile;

    [DataField]
    public float SafetyRadius;

    [DataField]
    public ProtoId<KoronusSafetyProfilePrototype>? LocalSafetyProfile;

    [DataField]
    public bool ProtectGrid;
}
