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
    /// Adds the explicit infrastructure protection components to authored facility grids.
    /// Procedural terrain and asteroid grids never receive these components automatically.
    /// </summary>
    [DataField]
    public bool ProtectInitialGrid;

    [DataField]
    public bool Enabled;

    [DataField]
    public float BoundaryRadius = 5000f;

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
