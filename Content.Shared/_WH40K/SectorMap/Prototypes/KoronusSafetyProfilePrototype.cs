using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.SectorMap.Prototypes;

/// <summary>
/// Reusable safety policy for a Koronus system or a local protected area within it.
/// All active profiles are additive: a restriction from any profile wins.
/// </summary>
[Prototype("koronusSafetyProfile")]
public sealed partial class KoronusSafetyProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public bool BlockShipWeapons;

    [DataField]
    public bool BlockPlayerDamage;

    [DataField]
    public bool BlockPlayerStaminaDamage;

    [DataField]
    public bool BlockPlayerPulling;

    [DataField]
    public bool BlockOtherPlayerStrip;

    [DataField]
    public bool BlockPlayerExplosions;

    [DataField]
    public bool ProtectPlayerShipWires;

    [DataField]
    public bool ProtectPlayerShipDeconstruction;

    [DataField]
    public bool ProtectStation;

    [DataField]
    public bool BlockForcedBuckle;

    /// <summary>
    /// Blocks non-damaging hostile actions against players and protected non-hostile mobs.
    /// </summary>
    [DataField]
    public bool BlockPlayerHarmfulInteractions;

    /// <summary>
    /// When enabled, players may not damage mobs that are not hostile to them.
    /// Hostile mobs and generated terrain remain valid targets.
    /// </summary>
    [DataField]
    public bool ProtectNonHostileMobs;

    /// <summary>
    /// Separately controls forced unbuckling. This is intentionally disabled for the normal
    /// safe-zone profile so that rescuers can free a player from a chair.
    /// </summary>
    [DataField]
    public bool BlockForcedUnbuckle;

    /// <summary>
    /// Removes puddles after a short delay. This is intended for busy public facilities where
    /// deliberate floor flooding is disruptive, not for an entire orbital system.
    /// </summary>
    [DataField]
    public bool AutoCleanPuddles;

    /// <summary>
    /// Prevents players from opening maintenance panels with a screwdriver.
    /// </summary>
    [DataField]
    public bool BlockMaintenancePanelScrewdriving;

    /// <summary>
    /// Prevents manual anchoring and unanchoring through the standard anchoring tool action.
    /// </summary>
    [DataField]
    public bool BlockAnchoring;

    /// <summary>
    /// Prevents construction-graph deconstruction, independently of broader infrastructure protection.
    /// </summary>
    [DataField]
    public bool BlockDeconstruction;

    /// <summary>
    /// Prevents deploying entities from hand-held placement items.
    /// </summary>
    [DataField]
    public bool BlockHandheldEntityPlacement;

    /// <summary>
    /// Prevents sprays and spawned smoke or foam from applying chemical effects.
    /// </summary>
    [DataField]
    public bool BlockChemicalEffects;

    /// <summary>
    /// Prevents firing ammunition whose projectile creates a radiation source.
    /// </summary>
    [DataField]
    public bool BlockRadiationMunitions;

    /// <summary>
    /// Removes artifacts after a short grace period.
    /// </summary>
    [DataField]
    public bool AutoCleanupArtifacts;

    /// <summary>
    /// Removes hostile active NPCs after a short grace period.
    /// </summary>
    [DataField]
    public bool AutoCleanupHostileNpcs;

    /// <summary>
    /// Prevents a player's body from being dragged into a disposal unit.
    /// </summary>
    [DataField]
    public bool BlockPlayerDisposal;

    /// <summary>
    /// Prevents portable gas sources from releasing their contents into the station atmosphere.
    /// </summary>
    [DataField]
    public bool BlockAtmosphericRelease;

    public KoronusSafetyRule Rules
    {
        get
        {
            var rules = KoronusSafetyRule.None;
            if (BlockShipWeapons)
                rules |= KoronusSafetyRule.ShipWeapons;
            if (BlockPlayerDamage)
                rules |= KoronusSafetyRule.PlayerDamage;
            if (BlockPlayerStaminaDamage)
                rules |= KoronusSafetyRule.PlayerStaminaDamage;
            if (BlockPlayerPulling)
                rules |= KoronusSafetyRule.PlayerPulling;
            if (BlockOtherPlayerStrip)
                rules |= KoronusSafetyRule.OtherPlayerStrip;
            if (BlockPlayerExplosions)
                rules |= KoronusSafetyRule.PlayerExplosions;
            if (ProtectPlayerShipWires)
                rules |= KoronusSafetyRule.PlayerShipWires;
            if (ProtectPlayerShipDeconstruction)
                rules |= KoronusSafetyRule.PlayerShipDeconstruction;
            if (ProtectStation)
                rules |= KoronusSafetyRule.StationProtection;
            if (BlockForcedBuckle)
                rules |= KoronusSafetyRule.ForcedBuckle;
            if (BlockPlayerHarmfulInteractions)
                rules |= KoronusSafetyRule.PlayerHarmfulInteractions;
            if (ProtectNonHostileMobs)
                rules |= KoronusSafetyRule.ProtectNonHostileMobs;
            if (BlockForcedUnbuckle)
                rules |= KoronusSafetyRule.ForcedUnbuckle;
            if (AutoCleanPuddles)
                rules |= KoronusSafetyRule.PuddleAutoCleanup;
            if (BlockMaintenancePanelScrewdriving)
                rules |= KoronusSafetyRule.MaintenancePanelScrewdriving;
            if (BlockAnchoring)
                rules |= KoronusSafetyRule.Anchoring;
            if (BlockDeconstruction)
                rules |= KoronusSafetyRule.Deconstruction;
            if (BlockHandheldEntityPlacement)
                rules |= KoronusSafetyRule.HandheldEntityPlacement;
            if (BlockChemicalEffects)
                rules |= KoronusSafetyRule.ChemicalEffects;
            if (BlockRadiationMunitions)
                rules |= KoronusSafetyRule.RadiationMunitions;
            if (AutoCleanupArtifacts)
                rules |= KoronusSafetyRule.ArtifactAutoCleanup;
            if (AutoCleanupHostileNpcs)
                rules |= KoronusSafetyRule.HostileNpcAutoCleanup;
            if (BlockPlayerDisposal)
                rules |= KoronusSafetyRule.PlayerDisposal;
            if (BlockAtmosphericRelease)
                rules |= KoronusSafetyRule.AtmosphericRelease;
            return rules;
        }
    }
}

/// <summary>
/// A circular safety area authored in system-map coordinates.
/// </summary>
[DataDefinition]
public sealed partial class KoronusSafetyZoneDefinition
{
    [DataField(required: true)]
    public ProtoId<KoronusSafetyProfilePrototype> Profile;

    [DataField]
    public Vector2 Center;

    [DataField]
    public float Radius;
}

[Flags]
public enum KoronusSafetyRule : uint
{
    None = 0,
    ShipWeapons = 1 << 0,
    PlayerDamage = 1 << 1,
    PlayerStaminaDamage = 1 << 2,
    PlayerPulling = 1 << 3,
    OtherPlayerStrip = 1 << 4,
    PlayerExplosions = 1 << 5,
    PlayerShipWires = 1 << 6,
    PlayerShipDeconstruction = 1 << 7,
    StationProtection = 1 << 8,
    ForcedBuckle = 1 << 9,
    PlayerHarmfulInteractions = 1 << 10,
    ProtectNonHostileMobs = 1 << 11,
    ForcedUnbuckle = 1 << 12,
    PuddleAutoCleanup = 1 << 13,
    MaintenancePanelScrewdriving = 1 << 14,
    Anchoring = 1 << 15,
    Deconstruction = 1 << 16,
    HandheldEntityPlacement = 1 << 17,
    ChemicalEffects = 1 << 18,
    RadiationMunitions = 1 << 19,
    ArtifactAutoCleanup = 1 << 20,
    HostileNpcAutoCleanup = 1 << 21,
    PlayerDisposal = 1 << 22,
    AtmosphericRelease = 1 << 23,
}
