using Content.Shared._WH40K.Biotype;
using Content.Shared.Body.Components;
using Content.Shared.Humanoid;
using Content.Shared.PowerCell;

namespace Content.Shared._WH40K.Synthetics;

public abstract class SharedWh40kSyntheticsSystem : EntitySystem
{
    [Dependency] private readonly SharedWh40kBiotypeSystem _biotype = default!;

    /// <summary>
    /// True for humanoid Cybernetic (Mechanicus) and IPC chassis bodies (excluding borgs/drones).
    /// </summary>
    public bool IsSyntheticChassis(EntityUid body)
    {
        return HasComp<HumanoidAppearanceComponent>(body) &&
               HasComp<BodyComponent>(body) &&
               _biotype.IsCybernetic(body);
    }

    public bool IsMechanicalPart(EntityUid part)
    {
        return _biotype.IsSyntheticPart(part);
    }

    /// <summary>Whether the component can be installed or removed through the service bay without an operating table.</summary>
    protected bool CanServiceInField(EntityUid item)
    {
        return HasComp<PowerCellComponent>(item) || HasComp<Wh40kRadCollectorComponent>(item);
    }
}
