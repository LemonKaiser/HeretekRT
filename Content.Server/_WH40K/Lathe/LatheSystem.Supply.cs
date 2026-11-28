using System.Linq;
using Content.Server._WH40K.Lathe;
using Content.Server.Storage.Components;
using Content.Shared._WH40K.Lathe;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Lathe;

public sealed partial class LatheSystem
{
    [Dependency] private Wh40kFabricationSiloSystem _fabricationSilo = default!;

    public int CountStoredEntities(EntityUid uid, EntProtoId prototype)
    {
        var count = _fabricationSilo.GetPartAmount(uid, prototype);
        if (!TryComp<EntityStorageComponent>(uid, out var storage))
            return count;

        foreach (var stored in storage.Contents.ContainedEntities)
        {
            if (_fabricationSilo.IsRecipePart(stored, prototype))
                count += _stackQuery.TryComp(stored, out var stack) ? stack.Count : 1;
        }

        return count;
    }

    private void ConsumeRecipeEntities(EntityUid uid, EntProtoId prototype, int amount)
    {
        var remaining = amount;
        if (TryComp<EntityStorageComponent>(uid, out var storage))
        {
            foreach (var stored in storage.Contents.ContainedEntities.ToArray())
            {
                if (!_fabricationSilo.IsRecipePart(stored, prototype))
                    continue;

                _stackQuery.TryComp(stored, out var stack);
                var count = stack?.Count ?? 1;
                var take = Math.Min(remaining, count);
                if (take < count)
                {
                    _stack.SetCount(stored, count - take, stack);
                }
                else
                {
                    _container.Remove(stored, storage.Contents);
                    QueueDel(stored);
                }

                remaining -= take;
                if (remaining <= 0)
                    return;
            }
        }

        _fabricationSilo.ConsumeParts(uid, prototype, remaining);
    }

    private void ConsumeRecipeReagent(EntityUid uid, LatheComponent component, ProtoId<ReagentPrototype> reagent, FixedPoint2 amount)
    {
        var local = FixedPoint2.Min(GetSlotReagent(uid, component, reagent, out var solution), amount);
        if (local > FixedPoint2.Zero && solution != null)
            _solution.SplitSolutionPerReagentWithOnly(solution.Value, local, reagent);

        if (amount - local > FixedPoint2.Zero)
            _fabricationSilo.ConsumeReagent(uid, reagent, amount - local);
    }

    private bool CanQueue(LatheComponent component, LatheRecipePrototype recipe, int quantity)
    {
        if (component.Queue.Count > 0 && component.Queue[^1].Recipe.ID == recipe.ID)
            return component.Queue[^1].ItemsRequested <= LatheRecipeBatch.MaxItemsRequested - quantity;

        return component.Queue.Count < LatheComponent.MaxQueuedBatches;
    }

    private int FindStartableBatch(EntityUid uid, LatheComponent component)
    {
        var designs = GetAvailableRecipes(uid, component).ToHashSet();
        return component.Queue.FindIndex(batch => designs.Contains(batch.Recipe.ID) && HasSupplies(uid, batch.Recipe, component));
    }

    private bool HasSupplies(EntityUid uid, LatheRecipePrototype recipe, LatheComponent component)
    {
        foreach (var (material, amount) in recipe.Materials)
        {
            if (_materialStorage.GetMaterialAmount(uid, material) <
                AdjustMaterial(amount, recipe.MaterialDiscountScale, component.FinalMaterialUseMultiplier))
                return false;
        }

        foreach (var (entity, needed) in recipe.Entities)
        {
            if (CountStoredEntities(uid, entity) < needed)
                return false;
        }

        foreach (var (reagent, needed) in recipe.Reagents)
        {
            if (GetAvailableReagent(uid, component, reagent) < needed)
                return false;
        }

        return true;
    }

    private Wh40kLatheMissingSupplies GetMissingSupplies(EntityUid uid, LatheRecipePrototype recipe, LatheComponent component)
    {
        var missing = new Wh40kLatheMissingSupplies();
        foreach (var (material, amount) in recipe.Materials)
        {
            var shortage = AdjustMaterial(amount, recipe.MaterialDiscountScale, component.FinalMaterialUseMultiplier) -
                           _materialStorage.GetMaterialAmount(uid, material);
            if (shortage > 0)
                missing.Materials[material] = shortage;
        }

        foreach (var (entity, needed) in recipe.Entities)
        {
            var shortage = needed - CountStoredEntities(uid, entity);
            if (shortage > 0)
                missing.Entities[entity] = shortage;
        }

        foreach (var (reagent, needed) in recipe.Reagents)
        {
            var shortage = needed - GetAvailableReagent(uid, component, reagent);
            if (shortage > FixedPoint2.Zero)
                missing.Reagents[reagent] = shortage;
        }

        return missing;
    }

    private void FillSupplyState(EntityUid uid, LatheComponent component, LatheUpdateState state)
    {
        state.PrintingBatch = component.CurrentRecipe != null ? component.PrintingBatch : null;

        if (!_uiSys.IsUiOpen(uid, LatheUiKey.Key))
            return;

        foreach (var id in state.Recipes)
        {
            if (_proto.TryIndex(id, out var recipe) && HasSupplies(uid, recipe, component))
                state.ReadyRecipes.Add(id);
        }

        var designs = state.Recipes.ToHashSet();
        foreach (var batch in component.Queue)
        {
            var missing = GetMissingSupplies(uid, batch.Recipe, component);
            missing.DesignAvailable = designs.Contains(batch.Recipe.ID);
            state.QueueSupplies[batch.Index] = missing;
        }
    }
}
