using Robust.Shared.Map;

namespace Content.Shared._WH40K.Construction;

[ByRefEvent]
public record struct StructureConstructionAttemptEvent(EntityUid User, EntityCoordinates Coordinates, bool ShowPopup = true)
{
    public bool Cancelled;
}
