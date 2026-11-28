using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Localizations;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.WolfChem.Narcotics.Delivery;

/// <summary>
/// Keeps anything but the baggie's listed powders out of it, and handles its solids: a click puts one in, using the
/// baggie in hand takes the last one out, and examining it lists them.
/// </summary>
public sealed class Wh40kBaggieSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedStorageSystem _storage = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kBaggieComponent, SolutionTransferAttemptEvent>(OnTransferAttempt);
        // Before the spiker, which would empty a pill into it.
        SubscribeLocalEvent<Wh40kBaggieComponent, InteractUsingEvent>(OnInteractUsing,
            before: new[] { typeof(SolutionSpikerSystem), typeof(SharedStorageSystem) });
        SubscribeLocalEvent<Wh40kBaggieComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<Wh40kBaggieComponent, ExaminedEvent>(OnExamined);
    }

    private void OnTransferAttempt(Entity<Wh40kBaggieComponent> ent, ref SolutionTransferAttemptEvent args)
    {
        if (args.To != ent.Owner || !_solution.TryGetDrainableSolution(args.From, out _, out var solution))
            return;

        if (Refused(ent.Comp, solution) is { } reason)
            args.Cancel(reason);
    }

    private void OnInteractUsing(Entity<Wh40kBaggieComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp<StorageComponent>(ent, out var storage) && _storage.CanInsert(ent, args.Used, out _, storage))
        {
            args.Handled = true;
            if (_storage.PlayerInsertHeldEntity((ent, storage), args.User))
                _popup.PopupClient(Loc.GetString("wh40k-baggie-solid-in", ("item", args.Used)), ent, args.User);
            return;
        }

        if (!TryComp<SolutionSpikerComponent>(args.Used, out var spiker)
            || !_solution.TryGetSolution(args.Used, spiker.SourceSolution, out _, out var solution)
            || Refused(ent.Comp, solution) is not { } reason)
            return;

        _popup.PopupClient(reason, ent, args.User);
        args.Handled = true;
    }

    private void OnUseInHand(Entity<Wh40kBaggieComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryTakeSolid(ent, out var solid))
        {
            if (_solution.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var powder)
                && powder.Volume > 0)
            {
                args.Handled = true;
                _popup.PopupClient(Loc.GetString("wh40k-baggie-powder-hint"), ent, args.User);
            }

            return;
        }

        args.Handled = true;
        _hands.PickupOrDrop(args.User, solid, dropNear: true);
        if (TryComp<StorageComponent>(ent, out var storage))
            _audio.PlayPredicted(storage.StorageRemoveSound, ent, args.User);
        _popup.PopupClient(Loc.GetString("wh40k-baggie-solid-out", ("item", solid)), ent, args.User);
    }

    private void OnExamined(Entity<Wh40kBaggieComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange
            || !TryComp<StorageComponent>(ent, out var storage)
            || storage.Container.ContainedEntities.Count == 0)
            return;

        var solids = storage.Container.ContainedEntities
            .GroupBy(solid => Name(solid))
            .Select(group => group.Count() == 1
                ? group.Key
                : Loc.GetString("wh40k-baggie-solid-count", ("count", group.Count()), ("name", group.Key)))
            .ToList();

        args.PushMarkup(Loc.GetString("wh40k-baggie-holds", ("solids", ContentLocalizationManager.FormatList(solids))));
    }

    /// <summary>Takes the last solid put in out of the baggie, leaving it loose where the baggie is.</summary>
    public bool TryTakeSolid(EntityUid baggie, out EntityUid solid)
    {
        solid = default;
        if (!TryComp<StorageComponent>(baggie, out var storage) || storage.Container.ContainedEntities.Count == 0)
            return false;

        solid = storage.Container.ContainedEntities[^1];
        return _container.Remove(solid, storage.Container);
    }

    /// <summary>The refusal message if the solution holds anything the baggie doesn't take, else null.</summary>
    public string? Refused(Wh40kBaggieComponent baggie, Solution solution)
    {
        if (baggie.Reagents is not { } allowed)
            return null;

        var refused = solution.Contents
            .Select(reagent => reagent.Reagent.Prototype)
            .Where(reagent => !allowed.Contains(reagent))
            .Distinct()
            .Select(reagent => _prototype.TryIndex(reagent, out var proto)
                ? proto.LocalizedName
                : reagent.Id)
            .ToList();

        return refused.Count == 0
            ? null
            : Loc.GetString("wh40k-baggie-refused", ("reagents", ContentLocalizationManager.FormatList(refused)));
    }
}
