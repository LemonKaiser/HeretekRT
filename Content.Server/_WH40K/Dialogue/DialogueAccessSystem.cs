using System.Linq;
using Content.Server.Access.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared._WH40K.Dialogue;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Dialogue;

/// <summary>
/// Resolves a player's ID card for dialogue scripts and changes only the requested access tags.
/// It never removes a card from a PDA or a hand; the hand-off scene will be handled separately.
/// </summary>
public sealed class DialogueAccessSystem : EntitySystem
{
    [Dependency] private AccessSystem _access = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    /// <summary>
    /// Adds or removes exact access levels on an ID card selected from the player.
    /// </summary>
    public bool TryModifyAccess(
        EntityUid owner,
        DialogueAccessCardSource source,
        IReadOnlyCollection<ProtoId<AccessLevelPrototype>> accesses,
        bool add)
    {
        if (!CanModifyAccess(owner, source, accesses, add)
            || !TryFindIdCard(owner, source, out var card)
            || !TryComp(card, out AccessComponent? accessComponent))
            return false;

        var tags = new HashSet<ProtoId<AccessLevelPrototype>>(accessComponent.Tags);
        if (add)
            tags.UnionWith(accesses);
        else
            tags.ExceptWith(accesses);

        return _access.TrySetTags(card, tags, accessComponent);
    }

    /// <summary>
    /// Checks that the requested modification can be performed without changing the card.
    /// Removing access is all-or-nothing: every requested tag must already be present.
    /// </summary>
    public bool CanModifyAccess(
        EntityUid owner,
        DialogueAccessCardSource source,
        IReadOnlyCollection<ProtoId<AccessLevelPrototype>> accesses,
        bool add)
    {
        if (Deleted(owner)
            || accesses.Count == 0
            || accesses.Any(access => !_prototypeManager.HasIndex<AccessLevelPrototype>(access))
            || !TryFindIdCard(owner, source, out var card)
            || !TryComp(card, out AccessComponent? accessComponent))
        {
            return false;
        }

        return add || accesses.All(accessComponent.Tags.Contains);
    }

    /// <summary>
    /// Finds a direct ID card in a hand or an ID card stored inside a PDA, according to the configured source.
    /// </summary>
    public bool TryFindIdCard(EntityUid owner, DialogueAccessCardSource source, out EntityUid card)
    {
        switch (source)
        {
            case DialogueAccessCardSource.Auto:
                return TryFindHeldCard(owner, out card)
                       || TryFindPdaCard(owner, out card);
            case DialogueAccessCardSource.Pda:
                return TryFindPdaCard(owner, out card);
            case DialogueAccessCardSource.Hands:
                return TryFindHeldCard(owner, out card);
            default:
                card = default;
                return false;
        }
    }

    private bool TryFindHeldCard(EntityUid owner, out EntityUid card)
    {
        foreach (var held in _hands.EnumerateHeld(owner))
        {
            if (!TryComp<IdCardComponent>(held, out _))
                continue;

            card = held;
            return true;
        }

        card = default;
        return false;
    }

    private bool TryFindPdaCard(EntityUid owner, out EntityUid card)
    {
        if (_inventory.TryGetSlotEntity(owner, "id", out var equipped)
            && TryGetPdaCard(equipped.Value, out card))
        {
            return true;
        }

        foreach (var held in _hands.EnumerateHeld(owner))
        {
            if (TryGetPdaCard(held, out card))
                return true;
        }

        card = default;
        return false;
    }

    private bool TryGetPdaCard(EntityUid pdaUid, out EntityUid card)
    {
        if (TryComp(pdaUid, out PdaComponent? pda)
            && pda.ContainedId is { } contained
            && TryComp<IdCardComponent>(contained, out _))
        {
            card = contained;
            return true;
        }

        card = default;
        return false;
    }
}
