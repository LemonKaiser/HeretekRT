using System.Diagnostics.CodeAnalysis;
using Content.Shared._WH40K.Lathe;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Lathe;

public sealed partial class Wh40kFabricationSiloSystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    private static readonly FixedPoint2 DiscardLogThreshold = FixedPoint2.New(10);

    private void InitializeChemicals()
    {
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, WithdrawWh40kFabricationSiloReagentMessage>(OnWithdrawReagent);
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, DiscardWh40kFabricationSiloReagentMessage>(OnDiscardReagent);
    }

    public FixedPoint2 WithdrawReagent(Entity<Wh40kFabricationSiloComponent> ent,
        EntityUid user,
        ProtoId<ReagentPrototype> reagent,
        FixedPoint2? amount)
    {
        if (amount is { } requested && requested <= FixedPoint2.Zero ||
            !TryGetStock(ent, user, reagent, out var soln, out var solution, out var stored))
            return FixedPoint2.Zero;

        if (!TryGetSlottedSolution(ent, out var container, out var target, out var targetSolution))
        {
            _popup.PopupEntity(Loc.GetString("fabrication-silo-no-container"), ent, user);
            return FixedPoint2.Zero;
        }

        var take = FixedPoint2.Min(stored, targetSolution.AvailableVolume);
        if (amount != null)
            take = FixedPoint2.Min(take, amount.Value);

        if (take <= FixedPoint2.Zero)
        {
            _popup.PopupEntity(Loc.GetString("fabrication-silo-container-full", ("container", container)), ent, user);
            return FixedPoint2.Zero;
        }

        var moved = solution.SplitSolutionWithOnly(take, reagent.Id);
        _solutions.UpdateChemicals(soln.Value);
        _solutions.TryAddSolution(target.Value, moved);

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} withdrew {SharedSolutionContainerSystem.ToPrettyString(moved)} from {ToPrettyString(ent.Owner):entity} into {ToPrettyString(container):target}");

        return moved.Volume;
    }

    public FixedPoint2 DiscardReagent(Entity<Wh40kFabricationSiloComponent> ent, EntityUid user, ProtoId<ReagentPrototype> reagent)
    {
        if (!TryGetStock(ent, user, reagent, out var soln, out var solution, out var stored))
            return FixedPoint2.Zero;

        var removed = solution.SplitSolutionWithOnly(stored, reagent.Id);
        _solutions.UpdateChemicals(soln.Value);

        if (removed.Volume >= DiscardLogThreshold)
        {
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(user):player} discarded {SharedSolutionContainerSystem.ToPrettyString(removed)} from {ToPrettyString(ent.Owner):entity}");
        }

        return removed.Volume;
    }

    private bool TryGetStock(Entity<Wh40kFabricationSiloComponent> ent,
        EntityUid user,
        ProtoId<ReagentPrototype> reagent,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution,
        out FixedPoint2 stored)
    {
        stored = FixedPoint2.Zero;
        if (!TryGetSolution(ent, out soln, out solution) ||
            !_prototypes.HasIndex(reagent) ||
            !_actionBlocker.CanInteract(user, ent.Owner) ||
            !_interaction.InRangeUnobstructed(user, ent.Owner))
            return false;

        stored = solution.GetTotalPrototypeQuantity(reagent.Id);
        return stored > FixedPoint2.Zero;
    }

    private void OnWithdrawReagent(Entity<Wh40kFabricationSiloComponent> ent, ref WithdrawWh40kFabricationSiloReagentMessage args)
    {
        WithdrawReagent(ent, args.Actor, args.Reagent, args.Amount);
    }

    private void OnDiscardReagent(Entity<Wh40kFabricationSiloComponent> ent, ref DiscardWh40kFabricationSiloReagentMessage args)
    {
        DiscardReagent(ent, args.Actor, args.Reagent);
    }
}
