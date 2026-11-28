using Content.Server.Administration.Logs;
using Content.Server.Lathe;
using Content.Server.Materials;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._WH40K.Lathe;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Tools.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Lathe;

/// <summary>
/// Loads recipe parts into lathes by direct use and allows editing queued batch totals.
/// </summary>
public sealed partial class Wh40kLatheSupplySystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kFabricationSiloClientComponent, InteractUsingEvent>(OnInteractUsing,
            before: new[] { typeof(MaterialStorageSystem) });
        SubscribeLocalEvent<LatheComponent, Wh40kLatheRecipeAmountMessage>(OnRecipeAmount);
        SubscribeLocalEvent<LatheComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    private void OnInteractUsing(Entity<Wh40kFabricationSiloClientComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            HasComp<ToolComponent>(args.Used) ||
            !TryComp<LatheComponent>(ent, out var lathe) ||
            !TryComp<EntityStorageComponent>(ent, out var storage) ||
            MetaData(args.Used).EntityPrototype?.ID is not { } part ||
            !UsesPart((ent, lathe), part))
            return;

        if (storage.Open && !_entityStorage.TryCloseStorage(ent))
            return;

        if (!_entityStorage.CanInsert(args.Used, ent, storage) ||
            !_entityStorage.AddToContents(args.Used, ent, storage))
            return;

        args.Handled = true;
        _lathe.TryStartProducing(ent, lathe);
        _lathe.UpdateUserInterfaceState(ent, lathe);
    }

    private void OnRecipeAmount(Entity<LatheComponent> ent, ref Wh40kLatheRecipeAmountMessage args)
    {
        var index = args.Index;
        var batchIndex = ent.Comp.Queue.FindIndex(batch => batch.Index == index);
        if (batchIndex < 0)
            return;

        var batch = ent.Comp.Queue[batchIndex];
        if (args.Amount <= 0 ||
            args.Amount > LatheRecipeBatch.MaxItemsRequested ||
            args.Amount < batch.ItemsPrinted)
            return;

        if (args.Amount > batch.ItemsRequested &&
            !_lathe.GetAvailableRecipes(ent, ent.Comp).Contains(batch.Recipe.ID))
            return;

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.Actor):player} changed a {_lathe.GetRecipeName(batch.Recipe)} batch from {batch.ItemsRequested} to {args.Amount} at {ToPrettyString(ent):lathe}");

        if (args.Amount == batch.ItemsPrinted)
            ent.Comp.Queue.RemoveAt(batchIndex);
        else
            batch.ItemsRequested = args.Amount;

        _lathe.TryStartProducing(ent, ent.Comp);
        _lathe.UpdateUserInterfaceState(ent, ent.Comp);
    }

    private void OnUiOpened(Entity<LatheComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(LatheUiKey.Key))
            return;

        _lathe.UpdateUserInterfaceState(ent, ent.Comp);
    }

    private bool UsesPart(Entity<LatheComponent> ent, EntProtoId part)
    {
        foreach (var id in _lathe.GetAvailableRecipes(ent, ent.Comp))
        {
            if (_proto.Index(id).Entities.ContainsKey(part))
                return true;
        }

        return false;
    }
}
