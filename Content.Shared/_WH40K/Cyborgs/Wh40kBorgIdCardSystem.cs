using Content.Shared._NF.Silicons.Borgs;
using Content.Shared.Access.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Containers;

namespace Content.Shared._WH40K.Cyborgs;

/// <summary>
/// Locates a cyborg's issued ID card and exposes its access to access readers even while its tool module is stowed.
/// </summary>
public sealed partial class Wh40kBorgIdCardSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>
    /// The chassis container that holds a crew cyborg's card until a tool module takes it.
    /// </summary>
    public const string ContainerId = "wh40k-borg-id";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);
    }

    /// <summary>
    /// Returns the cyborg's issued card whether it is held, stowed in a droppable module, or kept in the chassis.
    /// </summary>
    public bool TryGetCard(EntityUid borg, out EntityUid card)
    {
        card = default;
        if (!TryComp<BorgChassisComponent>(borg, out var chassis))
            return false;

        foreach (var held in _hands.EnumerateHeld(borg))
        {
            if (!HasComp<Wh40kBorgIdCardComponent>(held))
                continue;

            card = held;
            return true;
        }

        if (chassis.ModuleContainer != null)
        {
            foreach (var module in chassis.ModuleContainer.ContainedEntities)
            {
                if (TryComp<DroppableBorgModuleComponent>(module, out var droppable)
                    && _container.TryGetContainer(module, droppable.ContainerId, out var stowed)
                    && TryFindCard(stowed, out card))
                    return true;
            }
        }

        return _container.TryGetContainer(borg, ContainerId, out var own) && TryFindCard(own, out card);
    }

    /// <summary>
    /// Enumerates items stowed inside a cyborg's droppable modules.
    /// </summary>
    public IEnumerable<EntityUid> StowedItems(EntityUid borg)
    {
        if (!TryComp<BorgChassisComponent>(borg, out var chassis) || chassis.ModuleContainer == null)
            yield break;

        foreach (var module in chassis.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<DroppableBorgModuleComponent>(module, out var droppable)
                || !_container.TryGetContainer(module, droppable.ContainerId, out var stowed))
                continue;

            foreach (var item in stowed.ContainedEntities)
                yield return item;
        }
    }

    private bool TryFindCard(BaseContainer container, out EntityUid card)
    {
        foreach (var item in container.ContainedEntities)
        {
            if (!HasComp<Wh40kBorgIdCardComponent>(item))
                continue;

            card = item;
            return true;
        }

        card = default;
        return false;
    }

    private void OnGetAdditionalAccess(Entity<BorgChassisComponent> ent, ref GetAdditionalAccessEvent args)
    {
        if (TryGetCard(ent, out var card))
            args.Entities.Add(card);
    }
}
