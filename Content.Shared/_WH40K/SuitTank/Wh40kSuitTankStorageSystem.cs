using System.Diagnostics.CodeAnalysis;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Shared._WH40K.SuitTank;

/// <summary>
/// Finds the gas tanks stored inside a worn suit's internal storage and resolves their wearer.
/// </summary>
public sealed partial class Wh40kSuitTankStorageSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedGasTankSystem _gasTank = default!;
    [Dependency] private SharedItemSystem _item = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kSuitTankStorageComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<Wh40kSuitTankStorageComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<Wh40kSuitTankStorageComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnInserted(Entity<Wh40kSuitTankStorageComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            _item.VisualsChanged(ent);
    }

    private void OnRemoved(Entity<Wh40kSuitTankStorageComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            _item.VisualsChanged(ent);
    }

    private void OnUnequipped(Entity<Wh40kSuitTankStorageComponent> ent, ref GotUnequippedEvent args)
    {
        foreach (var tank in GetTanks(ent))
        {
            if (tank.Comp.User == args.Equipee)
                _gasTank.DisconnectFromInternals(tank, forced: true);
        }
    }

    /// <summary>
    /// Gets the gas tanks stored in a suit, in storage order.
    /// </summary>
    public IEnumerable<Entity<GasTankComponent>> GetTanks(EntityUid suit)
    {
        if (!_container.TryGetContainer(suit, StorageComponent.ContainerId, out var container))
            yield break;

        foreach (var item in container.ContainedEntities)
        {
            if (TryComp<GasTankComponent>(item, out var gas))
                yield return (item, gas);
        }
    }

    /// <summary>
    /// Gets the gas tanks stored in the suit worn in the outer clothing slot.
    /// </summary>
    public IEnumerable<Entity<GasTankComponent>> GetWornTanks(EntityUid wearer)
    {
        if (!_inventory.TryGetSlotEntity(wearer, "outerClothing", out var suit)
            || !HasComp<Wh40kSuitTankStorageComponent>(suit))
        {
            return [];
        }

        return GetTanks(suit.Value);
    }

    /// <summary>
    /// Gets the connectable worn suit tank with the highest pressure, optionally skipping one tank.
    /// </summary>
    public Entity<GasTankComponent>? FindFullestTank(EntityUid wearer, EntityUid? except = null)
    {
        Entity<GasTankComponent>? best = null;
        foreach (var tank in GetWornTanks(wearer))
        {
            if (tank.Owner == except || !_gasTank.CanConnectToInternals(tank))
                continue;

            var pressure = tank.Comp.Air?.Pressure ?? 0f;
            var bestPressure = best?.Comp.Air?.Pressure ?? float.NegativeInfinity;
            if (best == null || pressure > bestPressure)
                best = tank;
        }

        return best;
    }

    /// <summary>
    /// Gets the mob wearing the suit that holds this tank in its storage.
    /// </summary>
    public bool TryGetWearer(EntityUid tank, [NotNullWhen(true)] out EntityUid? wearer)
    {
        wearer = null;
        if (TerminatingOrDeleted(tank))
            return false;

        if (!_container.TryGetContainingContainer((tank, Transform(tank)), out var container)
            || container.ID != StorageComponent.ContainerId
            || !HasComp<Wh40kSuitTankStorageComponent>(container.Owner))
        {
            return false;
        }

        var parent = Transform(container.Owner).ParentUid;
        if (!parent.IsValid()
            || !_inventory.TryGetSlotEntity(parent, "outerClothing", out var wornSuit)
            || wornSuit != container.Owner)
        {
            return false;
        }

        wearer = parent;
        return true;
    }
}
