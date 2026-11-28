using System.Diagnostics.CodeAnalysis;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Lathe;

/// <summary>
/// Links lathes to parts and chemical silos under ore-silo rules and exposes stock lookups on both sides.
/// </summary>
public abstract partial class SharedWh40kFabricationSiloSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedOreSiloSystem _oreSilo = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;

    private EntityQuery<Wh40kFabricationSiloClientComponent> _clientQuery;
    private EntityQuery<Wh40kFabricationSiloComponent> _siloQuery;
    private EntityQuery<StackComponent> _stackQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, ComponentInit>(OnSiloInit);
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, ComponentShutdown>(OnSiloShutdown);
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, ToggleOreSiloClientMessage>(OnToggleClient);
        Subs.BuiEvents<Wh40kFabricationSiloComponent>(OreSiloUiKey.Key,
            subs => subs.Event<BoundUIOpenedEvent>(OnUiOpened));
        SubscribeLocalEvent<Wh40kFabricationSiloClientComponent, ComponentShutdown>(OnClientShutdown);

        _clientQuery = GetEntityQuery<Wh40kFabricationSiloClientComponent>();
        _siloQuery = GetEntityQuery<Wh40kFabricationSiloComponent>();
        _stackQuery = GetEntityQuery<StackComponent>();

        InitializeWhitelist();
        InitializeFilling();
    }

    public bool CanTransmit(Entity<Wh40kFabricationSiloComponent> silo, EntityUid client)
    {
        return _oreSilo.CanTransmit(silo.Owner, client, silo.Comp.Range);
    }

    public Entity<Wh40kFabricationSiloComponent>? GetLinkedSilo(EntityUid client, Wh40kFabricationSiloKind kind)
    {
        if (!_clientQuery.TryComp(client, out var link) ||
            GetLink(link, kind) is not { } uid ||
            !_siloQuery.TryComp(uid, out var silo) ||
            silo.Kind != kind ||
            !CanTransmit((uid, silo), client))
            return null;

        return (uid, silo);
    }

    public int GetPartAmount(EntityUid client, EntProtoId prototype)
    {
        if (GetLinkedSilo(client, Wh40kFabricationSiloKind.Parts) is not { Comp.Parts: { } parts })
            return 0;

        var count = 0;
        foreach (var part in parts.ContainedEntities)
        {
            if (IsRecipePart(part, prototype))
                count += GetItemCount(part);
        }

        return count;
    }

    public FixedPoint2 GetReagentAmount(EntityUid client, ProtoId<ReagentPrototype> reagent)
    {
        if (GetLinkedSilo(client, Wh40kFabricationSiloKind.Chemicals) is not { } silo ||
            !TryGetSolution(silo, out _, out var solution))
            return FixedPoint2.Zero;

        return solution.GetTotalPrototypeQuantity(reagent.Id);
    }

    public bool TryGetSolution(Entity<Wh40kFabricationSiloComponent> silo,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution)
    {
        soln = null;
        solution = null;
        return silo.Comp.Kind == Wh40kFabricationSiloKind.Chemicals &&
               _solution.TryGetSolution(silo.Owner, silo.Comp.SolutionName, out soln, out solution);
    }

    public bool TryGetSlottedSolution(Entity<Wh40kFabricationSiloComponent> silo,
        out EntityUid container,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution)
    {
        container = default;
        soln = null;
        solution = null;
        if (silo.Comp.Kind != Wh40kFabricationSiloKind.Chemicals ||
            _itemSlots.GetItemOrNull(silo.Owner, Wh40kFabricationSiloComponent.ContainerSlotId) is not { } item)
            return false;

        container = item;
        return _solution.TryGetFitsInDispenser(item, out soln, out solution);
    }

    public int GetItemCount(EntityUid part)
    {
        return _stackQuery.TryComp(part, out var stack) ? stack.Count : 1;
    }

    protected virtual void UpdateUi(Entity<Wh40kFabricationSiloComponent> ent)
    {
    }

    protected virtual void RefreshClient(EntityUid client)
    {
    }

    private void OnSiloInit(Entity<Wh40kFabricationSiloComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.Kind == Wh40kFabricationSiloKind.Parts)
            ent.Comp.Parts = _container.EnsureContainer<Container>(ent.Owner, Wh40kFabricationSiloComponent.PartsContainerId);
    }

    private void OnToggleClient(Entity<Wh40kFabricationSiloComponent> ent, ref ToggleOreSiloClientMessage args)
    {
        var client = GetEntity(args.Client);
        if (!_clientQuery.TryComp(client, out var clientComp))
            return;

        if (ent.Comp.Clients.Contains(client))
        {
            SetLink((client, clientComp), ent.Comp.Kind, null);
            ent.Comp.Clients.Remove(client);
            Dirty(ent);
        }
        else
        {
            if (!CanTransmit(ent, client))
                return;

            if (GetLink(clientComp, ent.Comp.Kind) is { } old && _siloQuery.TryComp(old, out var previous))
            {
                previous.Clients.Remove(client);
                Dirty(old, previous);
                UpdateUi((old, previous));
            }

            ent.Comp.Clients.Add(client);
            Dirty(ent);
            SetLink((client, clientComp), ent.Comp.Kind, ent.Owner);
        }

        UpdateUi(ent);
        RefreshClient(client);
    }

    private void OnUiOpened(Entity<Wh40kFabricationSiloComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnSiloShutdown(Entity<Wh40kFabricationSiloComponent> ent, ref ComponentShutdown args)
    {
        foreach (var client in ent.Comp.Clients)
        {
            if (!_clientQuery.TryComp(client, out var comp) || GetLink(comp, ent.Comp.Kind) != ent.Owner)
                continue;

            SetLink((client, comp), ent.Comp.Kind, null);
            RefreshClient(client);
        }
    }

    private void OnClientShutdown(Entity<Wh40kFabricationSiloClientComponent> ent, ref ComponentShutdown args)
    {
        foreach (var uid in new[] { ent.Comp.PartsSilo, ent.Comp.ChemicalSilo })
        {
            if (!_siloQuery.TryComp(uid, out var silo))
                continue;

            silo.Clients.Remove(ent);
            Dirty(uid.Value, silo);
            UpdateUi((uid.Value, silo));
        }
    }

    private static EntityUid? GetLink(Wh40kFabricationSiloClientComponent client, Wh40kFabricationSiloKind kind)
    {
        return kind == Wh40kFabricationSiloKind.Parts ? client.PartsSilo : client.ChemicalSilo;
    }

    private void SetLink(Entity<Wh40kFabricationSiloClientComponent> client, Wh40kFabricationSiloKind kind, EntityUid? silo)
    {
        if (kind == Wh40kFabricationSiloKind.Parts)
            client.Comp.PartsSilo = silo;
        else
            client.Comp.ChemicalSilo = silo;

        Dirty(client);
    }
}
