using Content.Shared._Mono.Emp;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Containers;

namespace Content.Shared._WH40K.BorgModules;

/// <summary>
/// Lends a borg chassis's <see cref="EmpResistanceComponent"/> to the power cell in its cell slot,
/// and updates the loan dynamically if the chassis gains or loses EMP resistance (e.g. via component modules).
/// </summary>
public sealed partial class CellHolderEmpResistanceSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PowerCellComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<PowerCellComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<EmpResistanceComponent, ComponentStartup>(OnHolderResistanceStartup);
        SubscribeLocalEvent<EmpResistanceComponent, ComponentShutdown>(OnHolderResistanceShutdown);
    }

    private void OnInserted(EntityUid uid, PowerCellComponent comp, EntGotInsertedIntoContainerMessage args)
    {
        var holder = args.Container.Owner;
        if (!HasComp<BorgChassisComponent>(holder) ||
            !TryComp<PowerCellSlotComponent>(holder, out var slot) ||
            args.Container.ID != slot.CellSlotId ||
            !TryComp<EmpResistanceComponent>(holder, out var holderResistance))
            return;

        LendResistance(uid, holderResistance);
    }

    private void OnRemoved(EntityUid uid, PowerCellComponent comp, EntGotRemovedFromContainerMessage args)
    {
        RevokeResistance(uid);
    }

    private void OnHolderResistanceStartup(Entity<EmpResistanceComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<BorgChassisComponent>(ent) ||
            !TryComp<PowerCellSlotComponent>(ent, out var slot) ||
            !_container.TryGetContainer(ent, slot.CellSlotId, out var container))
            return;

        foreach (var contained in container.ContainedEntities)
        {
            if (HasComp<PowerCellComponent>(contained))
                LendResistance(contained, ent.Comp);
        }
    }

    private void OnHolderResistanceShutdown(Entity<EmpResistanceComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent) ||
            !HasComp<BorgChassisComponent>(ent) ||
            !TryComp<PowerCellSlotComponent>(ent, out var slot) ||
            !_container.TryGetContainer(ent, slot.CellSlotId, out var container))
            return;

        foreach (var contained in container.ContainedEntities)
        {
            RevokeResistance(contained);
        }
    }

    private void LendResistance(EntityUid cell, EmpResistanceComponent holderResistance)
    {
        if (TryComp<EmpResistanceComponent>(cell, out var own))
        {
            if (own.Coefficient <= holderResistance.Coefficient)
                return;

            var lent = EnsureComp<LentEmpResistanceComponent>(cell);
            lent.OwnCoefficient ??= own.Coefficient;
            own.Coefficient = holderResistance.Coefficient;
            return;
        }

        AddComp(cell, new EmpResistanceComponent { Coefficient = holderResistance.Coefficient });
        EnsureComp<LentEmpResistanceComponent>(cell);
    }

    private void RevokeResistance(EntityUid cell)
    {
        if (!TryComp<LentEmpResistanceComponent>(cell, out var lent))
            return;

        if (lent.OwnCoefficient is { } own && TryComp<EmpResistanceComponent>(cell, out var resistance))
            resistance.Coefficient = own;
        else
            RemComp<EmpResistanceComponent>(cell);

        RemComp<LentEmpResistanceComponent>(cell);
    }
}

/// <summary>
/// Tracks an <see cref="EmpResistanceComponent"/> lent by a borg chassis to its slotted power cell.
/// </summary>
[RegisterComponent]
public sealed partial class LentEmpResistanceComponent : Component
{
    /// <summary>
    /// The cell's original EMP resistance coefficient prior to the loan, or null if it had none.
    /// </summary>
    [ViewVariables]
    public float? OwnCoefficient;
}
