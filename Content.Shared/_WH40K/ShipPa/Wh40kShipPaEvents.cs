using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.ShipPa;

[ByRefEvent]
public readonly record struct Wh40kShipAlertCodeChangedEvent(
    EntityUid Grid,
    ProtoId<Wh40kShipAlertCodePrototype> Old,
    ProtoId<Wh40kShipAlertCodePrototype> New,
    EntityUid? User);

[ByRefEvent]
public readonly record struct Wh40kShipGeneralQuartersChangedEvent(
    EntityUid Grid,
    bool Active,
    EntityUid? User);
