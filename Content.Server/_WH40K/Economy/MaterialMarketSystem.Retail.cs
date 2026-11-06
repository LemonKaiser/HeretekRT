using System.Linq;
using Content.Server.Construction.Components;
using Content.Server.Storage.Components;
using Content.Shared._NF.Trade;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.EntityTable.ValueSelector;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Economy;

public sealed partial class MaterialMarketSystem
{
    private readonly Dictionary<string, RetailValue> _retailValues = new();

    // Retail protects against the best permitted terminal and demand, even with no fees or saturation.
    // It never anchors the intrinsic material price or follows the current market cycle.
    public int GetPurchasePrice(string product, int offered)
    {
        if (!Enabled || !_prototypes.TryIndex<EntityPrototype>(product, out var prototype))
            return offered;
        EnsureTables();
        var value = GetRetailValue(prototype, new HashSet<string>());
        var ordinary = (value.Materials + value.Fixed) * Profile.MinimumRetailMarkup;
        var peak = (value.Materials * Profile.DemandBonusMaximum + value.Fixed) * Profile.MaximumCoefficient;
        var minimum = Math.Max(ordinary, peak * Profile.ResaleSafetyMarkup);
        if (!double.IsFinite(minimum))
            return int.MaxValue;
        return Math.Max(offered, (int) Math.Clamp(Math.Ceiling(minimum), 0, int.MaxValue));
    }

    private RetailValue GetRetailValue(string product, HashSet<string> path) =>
        _prototypes.TryIndex<EntityPrototype>(product, out var prototype) ? GetRetailValue(prototype, path) : RetailValue.Unbounded;

    private RetailValue GetRetailValue(EntityPrototype prototype, HashSet<string> path)
    {
        if (_retailValues.TryGetValue(prototype.ID, out var cached))
            return cached;
        var key = "entity:" + prototype.ID;
        if (!path.Add(key))
            return RetailValue.Unbounded;
        var count = prototype.TryComp<StackComponent>(out var stack, _factory) ? Math.Max(0, stack.Count) : 1;
        var materialValue = TryGetPrototypeMaterials(prototype, new HashSet<string>(), out var materials) ? BaseValue(materials) : 0;
        // Buying an inexpensive crafted item and recycling its physical composition must not bypass retail.
        if (prototype.TryComp<PhysicalCompositionComponent>(out var physical, _factory))
            materialValue = Math.Max(materialValue, physical.MaterialComposition.Sum(pair => pair.Value * _prices.GetValueOrDefault(pair.Key)));
        materialValue *= count;
        if (prototype.TryComp<MaterialStorageComponent>(out var storage, _factory))
            materialValue += storage.Storage.Sum(pair => Math.Max(0, pair.Value) * _prices.GetValueOrDefault(pair.Key.Id));
        var fixedValue = 0.0;
        if (prototype.TryComp<TradeCrateComponent>(out var trade, _factory))
            fixedValue = Math.Max(trade.ValueElsewhere, trade.ValueAtDestination + (double) trade.ExpressOnTimeBonus);
        else if (_specialGoods.TryGetValue(prototype.ID, out var catalogPrice) && !_latheRecipes.ContainsKey(prototype.ID) &&
                 !prototype.TryComp<ConstructionComponent>(out _, _factory))
            fixedValue = Math.Max(catalogPrice, _legacyPricing.GetEstimatedPrice(prototype));
        var value = new RetailValue(materialValue, fixedValue);

        // Include contents without spawning random entities during appraisal or opening a shop.
        var contents = new RetailValue();
        if (prototype.TryComp<StorageFillComponent>(out var fill, _factory))
            contents += GetRetailContents(fill.Contents, path);
        if (prototype.TryComp<ContainerFillComponent>(out var containers, _factory))
            foreach (var children in containers.Containers.Values)
                foreach (var child in children)
                    contents += GetRetailValue(child, path);
        if (prototype.TryComp<EntityTableContainerFillComponent>(out var tables, _factory))
            foreach (var table in tables.Containers.Values)
                contents += GetRetailTable(table, path);
        if (prototype.TryComp<ItemSlotsComponent>(out var slots, _factory))
            foreach (var slot in slots.Slots.Values)
                if (slot.StartingItem is { } child)
                    contents += GetRetailValue(child, path);
        if (prototype.TryComp<ComputerComponent>(out var computer, _factory) && computer.BoardPrototype is { } computerBoard)
            contents += GetRetailValue(computerBoard, path);
        if (prototype.TryComp<MachineComponent>(out var machine, _factory) && machine.Board is { } machineBoard &&
            _prototypes.TryIndex<EntityPrototype>(machineBoard.Id, out var board) && board.TryComp<MachineBoardComponent>(out var requirements, _factory))
        {
            contents += GetRetailValue(board, path);
            foreach (var (part, amount) in requirements.Requirements)
            {
                var partPrototype = machine.PartOverrides.TryGetValue(part, out var replacement)
                    ? replacement : _prototypes.Index(part).StockPartPrototype;
                contents += GetRetailValue(partPrototype.Id, path) * Math.Max(0, amount);
            }
            foreach (var info in requirements.ComponentRequirements.Values.Concat(requirements.TagRequirements.Values))
                contents += GetRetailValue(info.DefaultPrototype.Id, path) * Math.Max(0, info.Amount);
            foreach (var (stackType, amount) in requirements.StackRequirements)
            {
                var source = _prototypes.Index(stackType).Spawn;
                var sourcePrototype = _prototypes.Index(source);
                var spawnCount = sourcePrototype.TryComp<StackComponent>(out var sourceStack, _factory) ? Math.Max(1, sourceStack.Count) : 1;
                contents += GetRetailValue(sourcePrototype, path) * ((double) Math.Max(0, amount) / spawnCount);
            }
        }
        if (prototype.TryComp<BallisticAmmoProviderComponent>(out var ballistic, _factory) && ballistic.Proto is { } ammo)
            contents += ballistic.InfiniteUnspawned ? RetailValue.Unbounded : GetRetailValue(ammo.Id, path) * Math.Max(0, ballistic.Capacity);
        if (prototype.TryComp<RevolverAmmoProviderComponent>(out var revolver, _factory) && revolver.FillPrototype is { } cartridge)
            contents += GetRetailValue(cartridge, path) * Math.Max(0, revolver.Capacity);
        value += contents * count;
        if (prototype.TryComp<FlatpackComponent>(out var flatpack, _factory) && flatpack.Entity is { } unpacked)
            value = RetailValue.Max(value, GetRetailValue(unpacked.Id, path));
        if (prototype.TryComp<SpawnItemsOnUseComponent>(out var consumable, _factory) && consumable.Uses > 0)
        {
            var output = GetRetailContents(consumable.Items, path);
            value = output * (consumable.Uses - 1) + RetailValue.Max(value, output);
        }
        path.Remove(key);
        _retailValues[prototype.ID] = value;
        return value;
    }

    private RetailValue GetRetailContents(IEnumerable<EntitySpawnEntry> entries, HashSet<string> path)
    {
        // Mutually exclusive spawns use their maximum rather than their average or their sum.
        var value = new RetailValue();
        var groups = new Dictionary<string, RetailValue>();
        foreach (var entry in entries)
        {
            if (entry.PrototypeId is not { } child || entry.SpawnProbability <= 0)
                continue;
            var amount = GetRetailValue(child.Id, path) * Math.Max(0, Math.Max(entry.Amount, entry.MaxAmount));
            if (string.IsNullOrEmpty(entry.GroupId))
                value += amount;
            else
                groups[entry.GroupId] = RetailValue.Max(groups.GetValueOrDefault(entry.GroupId), amount);
        }
        foreach (var group in groups.Values)
            value += group;
        return value;
    }

    private RetailValue GetRetailTable(EntityTableSelector selector, HashSet<string> path)
    {
        if (selector.Prob <= 0)
            return new RetailValue();
        var value = new RetailValue();
        switch (selector)
        {
            case NoneSelector:
                break;
            case EntSelector entity:
                value = GetRetailValue(entity.Id.Id, path) * MaximumRolls(entity.Amount);
                break;
            case AllSelector all:
                foreach (var child in all.Children)
                    value += GetRetailTable(child, path);
                break;
            case GroupSelector group:
                foreach (var child in group.Children.Where(child => child.Weight > 0))
                    value = RetailValue.Max(value, GetRetailTable(child, path));
                break;
            case NestedSelector nested:
                var key = "table:" + nested.TableId.Id;
                if (!path.Add(key) || !_prototypes.TryIndex(nested.TableId, out var table))
                    return RetailValue.Unbounded;
                value = GetRetailTable(table.Table, path);
                path.Remove(key);
                break;
            default:
                return RetailValue.Unbounded;
        }
        return value * MaximumRolls(selector.Rolls);
    }

    private static double MaximumRolls(NumberSelector selector) => selector switch
    {
        ConstantNumberSelector constant => Math.Max(0, Math.Floor(constant.Value)),
        RangeNumberSelector range => Math.Max(0, Math.Ceiling(Math.Max(range.Range.X, range.Range.Y))),
        _ => double.PositiveInfinity,
    };

    private readonly record struct RetailValue(double Materials = 0, double Fixed = 0)
    {
        public static readonly RetailValue Unbounded = new(double.PositiveInfinity, double.PositiveInfinity);
        public static RetailValue operator +(RetailValue a, RetailValue b) => new(a.Materials + b.Materials, a.Fixed + b.Fixed);
        public static RetailValue operator *(RetailValue value, double count) => count <= 0 ? new RetailValue() :
            double.IsFinite(count) ? new RetailValue(value.Materials * count, value.Fixed * count) : Unbounded;
        public static RetailValue Max(RetailValue a, RetailValue b) => new(Math.Max(a.Materials, b.Materials), Math.Max(a.Fixed, b.Fixed));
    }
}
