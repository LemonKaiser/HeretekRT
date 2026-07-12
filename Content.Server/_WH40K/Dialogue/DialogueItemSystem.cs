using Content.Server.Stack;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Content.Shared._WH40K.Dialogue;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Dialogue;

/// <summary>
/// Counts and consumes dialogue payment items from hands or equipped slots. Storage contents, such as
/// backpacks and pockets, are intentionally outside of its scope.
/// </summary>
public sealed class DialogueItemSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    /// <summary>
    /// Counts matching items in the selected source. A stack contributes its current unit count.
    /// </summary>
    public int CountItems(EntityUid owner, DialogueItemSource source, EntProtoId prototype)
    {
        long count = 0;

        foreach (var candidate in EnumerateCandidates(owner, source, prototype))
        {
            if (!CanTakeCandidate(owner, source, candidate))
                continue;

            count += candidate.Units;
            if (count >= int.MaxValue)
                return int.MaxValue;
        }

        return (int) count;
    }

    /// <summary>
    /// Checks whether all requested units can be taken without changing the player's inventory.
    /// </summary>
    public bool CanTakeItems(EntityUid owner, DialogueItemSource source, EntProtoId prototype, int amount)
    {
        return amount > 0
               && !Deleted(owner)
               && CountItems(owner, source, prototype) >= amount;
    }

    /// <summary>
    /// Atomically consumes the requested number of matching units. If the full amount cannot be taken,
    /// no item is changed.
    /// </summary>
    public bool TryTakeItems(EntityUid owner, DialogueItemSource source, EntProtoId prototype, int amount)
    {
        if (!CanTakeItems(owner, source, prototype, amount))
            return false;

        var remaining = amount;
        var removals = new List<DialogueItemRemoval>();

        foreach (var candidate in EnumerateCandidates(owner, source, prototype))
        {
            if (!CanTakeCandidate(owner, source, candidate))
                continue;

            var units = Math.Min(candidate.Units, remaining);
            removals.Add(new DialogueItemRemoval(candidate.Item, candidate.Container, units));
            remaining -= units;

            if (remaining == 0)
                break;
        }

        if (remaining != 0)
            return false;

        foreach (var removal in removals)
        {
            if (TryComp(removal.Item, out StackComponent? stack) && removal.Units < stack.Count)
            {
                _stacks.SetCount(removal.Item, stack.Count - removal.Units, stack);
                continue;
            }

            // CanTakeCandidate has already run the regular hand/inventory checks. Removal without
            // reparenting prevents a payment item from appearing on the floor for a frame before deletion.
            if (!_containers.Remove(removal.Item, removal.Container, reparent: false, force: true))
                return false;

            QueueDel(removal.Item);
        }

        return true;
    }

    private IEnumerable<DialogueItemCandidate> EnumerateCandidates(
        EntityUid owner,
        DialogueItemSource source,
        EntProtoId prototype)
    {
        switch (source)
        {
            case DialogueItemSource.Hands:
                foreach (var item in _hands.EnumerateHeld(owner))
                {
                    if (!MatchesPrototype(item, prototype)
                        || !_hands.IsHolding(owner, item, out var hand)
                        || hand.Container == null)
                    {
                        continue;
                    }

                    yield return new DialogueItemCandidate(item, hand.Container, GetUnits(item));
                }

                yield break;
            case DialogueItemSource.Equipped:
                var slots = _inventory.GetSlotEnumerator(owner);
                while (slots.MoveNext(out var container))
                {
                    if (container.ContainedEntity is not { } item || !MatchesPrototype(item, prototype))
                        continue;

                    yield return new DialogueItemCandidate(item, container, GetUnits(item));
                }

                yield break;
            default:
                yield break;
        }
    }

    private bool CanTakeCandidate(EntityUid owner, DialogueItemSource source, DialogueItemCandidate candidate)
    {
        return source switch
        {
            DialogueItemSource.Hands => _hands.IsHolding(owner, candidate.Item, out var hand)
                                        && ReferenceEquals(hand.Container, candidate.Container)
                                        && _hands.CanDropHeld(owner, hand, checkActionBlocker: false),
            DialogueItemSource.Equipped => candidate.Container is ContainerSlot slot
                                            && _inventory.CanUnequip(owner, slot.ID, out _, slot),
            _ => false
        };
    }

    private bool MatchesPrototype(EntityUid item, EntProtoId prototype)
    {
        return !TerminatingOrDeleted(item) && MetaData(item).EntityPrototype?.ID == prototype.Id;
    }

    private int GetUnits(EntityUid item)
    {
        return TryComp(item, out StackComponent? stack) ? stack.Count : 1;
    }

    private readonly record struct DialogueItemCandidate(EntityUid Item, BaseContainer Container, int Units);
    private readonly record struct DialogueItemRemoval(EntityUid Item, BaseContainer Container, int Units);
}
