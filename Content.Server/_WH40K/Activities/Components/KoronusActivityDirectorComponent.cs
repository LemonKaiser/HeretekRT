using Content.Shared._WH40K.Activities;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Activities.Components;

/// <summary>
/// Per-round runtime state owned by the Koronus bootstrap game rule. Activities are added only
/// through the director so that map entities never become the authoritative source of progress.
/// </summary>
[RegisterComponent, Access(typeof(KoronusActivityDirectorSystem))]
public sealed partial class KoronusActivityDirectorComponent : Component
{
    [ViewVariables]
    public bool Active;

    [ViewVariables]
    public int RoundSeed;

    [ViewVariables]
    public int DayIndex;

    [ViewVariables]
    public KoronusActivityDayFocus DayFocus;

    [ViewVariables]
    public TimeSpan NextDayProfileAt;

    [ViewVariables]
    public long NextInstanceId = 1;

    [ViewVariables]
    public ProtoId<KoronusSectorPrototype>? Sector;

    [ViewVariables]
    public TimeSpan NextSpawnAt;

    [ViewVariables]
    public List<KoronusActivityMarkerState> Markers = [];

    [ViewVariables]
    public List<KoronusActivityRuntimeInstance> Instances = [];

    [ViewVariables]
    public List<KoronusActivityHistoryEntry> History = [];
}
