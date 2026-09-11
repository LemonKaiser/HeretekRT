using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Activities.Prototypes;

/// <summary>
/// A sector activity template. Default templates only publish a marker and dossier; a limited
/// stage-three executor may additionally name one audited objective root, never a map, grid,
/// reward, ghost role, or player spawn.
/// </summary>
[Prototype("koronusActivityTemplate")]
public sealed partial class KoronusActivityTemplatePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public KoronusActivityFamily Family;

    [DataField(required: true)]
    public KoronusActivityMarkerLane Lane;

    [DataField(required: true)]
    public KoronusActivityRisk Risk;

    [DataField]
    public KoronusActivityReliability Reliability = KoronusActivityReliability.Partial;

    [DataField(required: true)]
    public string TitleLocId = string.Empty;

    [DataField(required: true)]
    public string SummaryLocId = string.Empty;

    [DataField]
    public List<KoronusActivityDayFocus> PreferredDayFocus = [];

    [DataField]
    public int Weight = 1;

    [DataField]
    public float MinimumLifetime = 1200f;

    [DataField]
    public float MaximumLifetime = 2700f;

    [DataField]
    public float Cooldown = 1800f;

    /// <summary>
    /// Optional physical executor. A marker remains data-only until a living player reaches its
    /// eligible system or planetary surface; it never causes a remote map to load on its own.
    /// </summary>
    [DataField]
    public KoronusActivityExecutionKind Execution = KoronusActivityExecutionKind.None;

    /// <summary>
    /// Root entity materialized by <see cref="Execution"/>. It must be a single non-grid,
    /// non-ghost objective and is audited again after spawning.
    /// </summary>
    [DataField]
    public EntProtoId? ObjectivePrototype;

    /// <summary>
    /// Optional hostile profile. All fields are inert unless <see cref="Execution"/> is one of
    /// the explicitly allowed hostile executors; no general mob table exists.
    /// </summary>
    [DataField]
    public KoronusActivityThreatFamily ThreatFamily;

    [DataField]
    public KoronusActivityThreatLevel ThreatLevel;

    [DataField]
    public EntProtoId? HostilePrototype;

    [DataField]
    public int MinimumLivingParticipants = 1;

    [DataField]
    public int MinimumHostiles = 1;

    [DataField]
    public int MaximumHostiles = 1;

    [DataField]
    public float ParticipantRadius = 60f;

    [DataField]
    public float ThreatLeashRadius = 32f;

    [DataField]
    public float ThreatAbandonDelay = 45f;
}
