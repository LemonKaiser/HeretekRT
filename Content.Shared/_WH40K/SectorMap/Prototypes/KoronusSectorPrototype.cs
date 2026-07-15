using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.SectorMap.Prototypes;

/// <summary>
/// Static configuration for one authored sector map.
/// </summary>
[Prototype("koronusSector")]
public sealed partial class KoronusSectorPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The game map that becomes the engine-level DefaultMap for this sector.
    /// </summary>
    [DataField(required: true)]
    // GameMapPrototype is intentionally ignored by Content.YAMLLinter, so this stays a plain id.
    // KoronusSectorRuleSystem resolves it authoritatively during the round bootstrap.
    public string StartGameMap = string.Empty;

    [DataField(required: true)]
    public ProtoId<KoronusSystemPrototype> StartSystem;

    [DataField]
    public float DefaultBoundaryRadius = 5000f;

    [DataField]
    public float DefaultWarningFraction = 0.9f;

    [DataField]
    public float DefaultCleanupDelay = 10f;

    [DataField]
    public float DefaultWarningAnnouncementCooldown = 600f;
}
