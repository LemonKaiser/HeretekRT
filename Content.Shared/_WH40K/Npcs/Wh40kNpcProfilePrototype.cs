using Content.Shared._WH40K.Dialogue;
using Content.Shared._WH40K.Quests;
using Content.Shared.Dataset;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Npcs;

public enum Wh40kNpcKind : byte { Special, Worker, Resident }
public enum Wh40kNpcExclusivity : byte { Exclusive, DepartmentUnique, VenueUnique, NonExclusive, LimitedSet }
public enum Wh40kNpcService : byte { Quests, Trade, MedicalIntake, Technical, Investigation, Navigation, Archive, Faction, Social, Transit }

/// <summary>Narrative affiliation, not an SS14 combat faction or reputation record.</summary>
[Prototype]
public sealed partial class Wh40kNpcFactionPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
}

[Prototype]
public sealed partial class Wh40kNpcProfilePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public LocId Description;
    [DataField(required: true)] public string Role = string.Empty;
    [DataField(required: true)] public string Department = string.Empty;
    [DataField] public Wh40kNpcKind Kind;
    [DataField] public Wh40kNpcExclusivity Exclusivity;
    [DataField] public ProtoId<Wh40kNpcFactionPrototype>? Faction;
    [DataField] public HashSet<Wh40kNpcService> Services = [];
    [DataField] public HashSet<Wh40kQuestObjectiveKind> AllowedObjectives = [];
    [DataField] public ProtoId<StartingGearPrototype>? Gear;
    [DataField] public ProtoId<DialoguePrototype>? Dialogue;
    [DataField] public ProtoId<LocalizedDatasetPrototype>? Chatter;
    [DataField] public Wh40kNpcProtectionPolicy Protection = new();

    public bool RequiresUniqueAnchor => Exclusivity is Wh40kNpcExclusivity.Exclusive or
        Wh40kNpcExclusivity.DepartmentUnique or Wh40kNpcExclusivity.VenueUnique;
}

/// <summary>Stable authored identity. Marker and replaceable body reference this same prototype id.</summary>
[Prototype]
public sealed partial class Wh40kNpcAnchorPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public ProtoId<Wh40kNpcProfilePrototype> Profile;
}

/// <summary>Mandatory hub policy contract. Enforcement is implemented in the later protection stage.</summary>
[DataDefinition]
public sealed partial class Wh40kNpcProtectionPolicy
{
    [DataField] public bool Godmode = true;
    [DataField] public bool PreventCarry = true;
    [DataField] public bool LockEquipment = true;
    [DataField] public bool ReturnAcrossMaps = true;
    [DataField] public float HomeRadius = 3f;
}
