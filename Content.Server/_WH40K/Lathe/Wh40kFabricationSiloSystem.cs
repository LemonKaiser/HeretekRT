using System.Linq;
using Content.Server.Lathe;
using Content.Server.Pinpointer;
using Content.Server.Stack;
using Content.Shared._WH40K.Lathe;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Lathe;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Popups;
using Content.Shared.Power;
using Robust.Server.Containers;
using Robust.Server.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Lathe;

public sealed partial class Wh40kFabricationSiloSystem : SharedWh40kFabricationSiloSystem
{
    [Dependency] private ContainerSystem _containers = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private NavMapSystem _navMap = default!;
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private const float PreloadRangeSquared = 225f;

    private readonly HashSet<Entity<Wh40kFabricationSiloClientComponent>> _nearby = new();
    private readonly List<(EntityUid Client, EntityUid Silo)> _links = new();
    private readonly HashSet<EntityUid> _silosToAdd = new();
    private readonly HashSet<EntityUid> _silosToRemove = new();
    private float _updateTimer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, EjectWh40kFabricationSiloPartMessage>(OnEjectPart);
        InitializeChemicals();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateTimer += frameTime;
        if (_updateTimer < 1f)
            return;

        _updateTimer = 0f;

        var query = EntityQueryEnumerator<Wh40kFabricationSiloComponent>();
        while (query.MoveNext(out var uid, out var silo))
            UpdateUi((uid, silo));

        PreloadLinkedSilos();
    }

    public int ConsumeParts(EntityUid client, EntProtoId prototype, int amount)
    {
        if (amount <= 0 || GetLinkedSilo(client, Wh40kFabricationSiloKind.Parts) is not { Comp.Parts: { } parts } silo)
            return 0;

        var consumed = 0;
        foreach (var part in parts.ContainedEntities.ToArray())
        {
            if (!IsRecipePart(part, prototype))
                continue;

            var available = GetItemCount(part);
            var take = Math.Min(amount - consumed, available);
            if (take < available)
            {
                _stacks.SetCount(part, available - take);
            }
            else
            {
                _containers.Remove(part, parts);
                QueueDel(part);
            }

            consumed += take;
            if (consumed >= amount)
                break;
        }

        if (consumed > 0)
            RefreshClients(silo.Comp);

        return consumed;
    }

    public bool ConsumeReagent(EntityUid client, ProtoId<ReagentPrototype> reagent, FixedPoint2 amount)
    {
        if (GetLinkedSilo(client, Wh40kFabricationSiloKind.Chemicals) is not { } silo ||
            !TryGetSolution(silo, out var soln, out var solution) ||
            solution.GetTotalPrototypeQuantity(reagent.Id) < amount)
            return false;

        solution.SplitSolutionWithOnly(amount, reagent.Id);
        _solutions.UpdateChemicals(soln.Value);
        return true;
    }

    protected override void UpdateUi(Entity<Wh40kFabricationSiloComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, OreSiloUiKey.Key))
            return;

        var clients = new HashSet<(NetEntity, string, string)>();
        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Range, _nearby);
        foreach (var client in _nearby)
        {
            var link = ent.Comp.Kind == Wh40kFabricationSiloKind.Parts ? client.Comp.PartsSilo : client.Comp.ChemicalSilo;
            if (link != null || !CanTransmit(ent, client))
                continue;

            clients.Add(GetClientEntry(client, false, true));
        }

        foreach (var client in ent.Comp.Clients)
        {
            if (!TerminatingOrDeleted(client))
                clients.Add(GetClientEntry(client, true, CanTransmit(ent, client)));
        }

        _ui.SetUiState(ent.Owner, OreSiloUiKey.Key, new OreSiloBuiState(clients));
    }

    protected override void RefreshClient(EntityUid client)
    {
        if (TryComp<LatheComponent>(client, out var lathe))
            _lathe.UpdateUserInterfaceState(client, lathe);
    }

    private (NetEntity, string, string) GetClientEntry(EntityUid client, bool linked, bool inRange)
    {
        var beacon = _navMap.GetNearestBeaconString(client, onlyName: true);
        var text = Loc.GetString("ore-silo-ui-itemlist-entry",
            ("name", Identity.Name(client, EntityManager)),
            ("beacon", beacon),
            ("linked", linked),
            ("inRange", inRange));

        return (GetNetEntity(client), text, beacon);
    }

    private void PreloadLinkedSilos()
    {
        _links.Clear();
        var clientQuery = EntityQueryEnumerator<Wh40kFabricationSiloClientComponent>();
        while (clientQuery.MoveNext(out var uid, out var client))
        {
            if (client.PartsSilo is { } parts)
                _links.Add((uid, parts));
            if (client.ChemicalSilo is { } chemicals)
                _links.Add((uid, chemicals));
        }

        var actorQuery = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actorQuery.MoveNext(out _, out var actor, out var actorXform))
        {
            _silosToAdd.Clear();
            _silosToRemove.Clear();
            foreach (var (client, silo) in _links)
            {
                var clientXform = Transform(client);
                if (actorXform.GridUid == clientXform.GridUid &&
                    (actorXform.LocalPosition - clientXform.LocalPosition).LengthSquared() <= PreloadRangeSquared)
                    _silosToAdd.Add(silo);
                else
                    _silosToRemove.Add(silo);
            }

            _silosToRemove.ExceptWith(_silosToAdd);
            foreach (var silo in _silosToRemove)
                _pvsOverride.RemoveSessionOverride(silo, actor.PlayerSession);

            foreach (var silo in _silosToAdd)
                _pvsOverride.AddSessionOverride(silo, actor.PlayerSession);
        }
    }

    private void OnPowerChanged(Entity<Wh40kFabricationSiloComponent> ent, ref PowerChangedEvent args)
    {
        RefreshClients(ent.Comp);
    }

    private void OnSolutionChanged(Entity<Wh40kFabricationSiloComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId == ent.Comp.SolutionName)
            RefreshClients(ent.Comp);
    }

    private void OnEjectPart(Entity<Wh40kFabricationSiloComponent> ent, ref EjectWh40kFabricationSiloPartMessage args)
    {
        if (ent.Comp.Parts is not { } parts)
            return;

        var part = GetEntity(args.Part);
        if (parts.Contains(part) && _containers.Remove(part, parts))
            RefreshClients(ent.Comp);
    }
}
