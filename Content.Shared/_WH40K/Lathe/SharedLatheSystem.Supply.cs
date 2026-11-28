using Content.Shared._WH40K.Lathe;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.Lathe;

public abstract partial class SharedLatheSystem
{
    [Dependency] private SharedWh40kFabricationSiloSystem _siloStock = default!;

    public FixedPoint2 GetAvailableReagent(EntityUid uid, LatheComponent component, ProtoId<ReagentPrototype> reagent)
    {
        return GetSlotReagent(uid, component, reagent, out _) + _siloStock.GetReagentAmount(uid, reagent);
    }

    protected FixedPoint2 GetSlotReagent(EntityUid uid,
        LatheComponent component,
        ProtoId<ReagentPrototype> reagent,
        out Entity<SolutionComponent>? solution)
    {
        solution = null;
        if (component.ReagentOutputSlotId is not { } slotId ||
            !_container.TryGetContainer(uid, slotId, out var container) ||
            container.ContainedEntities.Count == 0 ||
            !_solution.TryGetDrainableSolution(container.ContainedEntities[0], out solution, out var contents))
            return FixedPoint2.Zero;

        return contents.GetTotalPrototypeQuantity(reagent.Id);
    }
}
