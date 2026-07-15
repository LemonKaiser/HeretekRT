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
public enum KoronusSafetyRule : ushort
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
}
