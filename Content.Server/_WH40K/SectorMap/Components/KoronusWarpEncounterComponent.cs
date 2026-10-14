using Content.Shared._WH40K.SectorMap;

namespace Content.Server._WH40K.SectorMap.Components;

[RegisterComponent, Access(typeof(Systems.KoronusWarpEncounterSystem))]
public sealed partial class KoronusWarpEncounterComponent : Component
{
    public KoronusWarpRisk Risk;
    public KoronusWarpIncident PlannedIncident;
    public KoronusWarpIncident Incident;
    public bool Triggered;
    public bool WaveSpawned;
    public bool Resolved;
    public bool LostBoarder;
    public bool SalvageReleased;
    public TimeSpan ExpiresAt;
    public TimeSpan BreachAt;
    public EntityUid? Rift;
    public List<EntityUid> OwnedEntities = [];
    public List<EntityUid> DisabledEquipment = [];
    public TimeSpan BlackoutUntil;
}

[RegisterComponent, Access(typeof(Systems.KoronusWarpEncounterSystem))]
public sealed partial class KoronusWarpRiftComponent : Component
{
    public EntityUid Shuttle;
}
