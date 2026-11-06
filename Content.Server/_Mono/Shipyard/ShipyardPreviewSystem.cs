using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

using static Content.Shared._Mono.Shipyard.SharedPreview;

namespace Content.Server._Mono.Shipyard;

/// <summary>
/// This handles preview map and preview observer.
/// </summary>
public sealed class ShipyardPreviewSystem : SharedShipyardPreviewSystem
{
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsolePreviewMessage>(OnPreview);
        SubscribeLocalEvent<ShipyardPreviewExitMessage>(OnDispose);
        SubscribeLocalEvent<VisitingMindComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<VisitingMindComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<VisitingMindComponent, MindUnvisitedMessage>(OnMindUnvisited);
    }

    private void OnPostGameMapLoad(PostGameMapLoad ev)
    {
        EnsureMap();
    }

    private void OnPreview(Entity<ShipyardConsoleComponent> ev, ref ShipyardConsolePreviewMessage evMsg)
    {
        if (!_prototypes.HasIndex(evMsg.Vessel))
            return;

        EnsureMap();
        if (!TryPreviewEntity(evMsg.Actor, out var observer))
            return;

        _ui.SetUiState(observer, ShipyardPreviewUiKey.Key, new ShipyardPreviewUserInterfaceState(evMsg.Vessel));
        _ui.OpenUi(observer, ShipyardPreviewUiKey.Key, observer);
    }

    private void EnsureMap()
    {
        if (CachePreviewMap())
            return;

        var mapUid = _map.CreateMap();
        var pMap = EnsureComp<PreviewMapComponent>(mapUid);

        _meta.SetEntityName(mapUid, "Shuttle preview map.");

        Dirty(mapUid, pMap);
    }

    private void OnDispose(ShipyardPreviewExitMessage ev)
    {
        ExitPreview(ev.Actor);
    }

    private void OnUiClosed(Entity<VisitingMindComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey.Equals(ShipyardPreviewUiKey.Key) && args.Actor == ent.Owner)
            ExitPreview(ent.Owner);
    }

    private void OnPlayerDetached(Entity<VisitingMindComponent> ent, ref PlayerDetachedEvent args)
    {
        ExitPreview(ent.Owner);
    }

    private void OnMindUnvisited(Entity<VisitingMindComponent> ent, ref MindUnvisitedMessage args)
    {
        if (MetaData(ent).EntityPrototype?.ID == "PreviewObserver")
            QueueDel(ent);
    }

    private void ExitPreview(EntityUid observer)
    {
        if (MetaData(observer).EntityPrototype?.ID != "PreviewObserver")
            return;

        if (!TryComp<VisitingMindComponent>(observer, out var mindComp) || !mindComp.MindId.HasValue)
            return;

        _mind.UnVisit(mindComp.MindId.Value);
        _ui.CloseUi(observer, ShipyardPreviewUiKey.Key);

        QueueDel(observer);
    }
}
