using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Pulling.Events;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Shared._WH40K.SectorMap;

/// <summary>Permissions for authored goods; ordinary interactions with public equipment remain available.</summary>
public sealed partial class SharedKoronusStationPropertySystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorage = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KoronusStationPropertyComponent, GettingPickedUpAttemptEvent>(OnPickup);
        SubscribeLocalEvent<KoronusStationPropertyComponent, BeingPulledAttemptEvent>(OnPull);
        SubscribeLocalEvent<KoronusStationPropertyComponent, ContainerGettingInsertedAttemptEvent>(OnInsert);
        SubscribeLocalEvent<KoronusStationPropertyComponent, ContainerGettingRemovedAttemptEvent>(OnRemove);
        SubscribeLocalEvent<KoronusStationPropertyComponent, GettingUsedAttemptEvent>(OnUse);
        SubscribeLocalEvent<KoronusStationPropertyComponent, GettingInteractedWithAttemptEvent>(OnInteract);
        SubscribeLocalEvent<KoronusStationPropertyComponent, ExaminedEvent>(OnExamined);
    }

    public bool IsProperty(EntityUid uid)
        => TryComp<KoronusStationPropertyComponent>(uid, out var property) &&
           property.HomeGrid != null && property.Policy != KoronusStationPropertyPolicy.Free;

    public bool CanMove(KoronusStationPropertyComponent property)
        => property.HomeGrid == null || property.Policy != KoronusStationPropertyPolicy.Display;

    public bool CanUse(EntityUid uid, KoronusStationPropertyComponent property)
        => CanMove(property) && (property.HomeGrid == null || property.Policy == KoronusStationPropertyPolicy.Free ||
                                Transform(uid).GridUid == property.HomeGrid);

    public bool CanInsert(KoronusStationPropertyComponent property, BaseContainer destination)
    {
        if (property.HomeGrid is not { } home || property.Policy == KoronusStationPropertyPolicy.Free)
            return true;

        if (Transform(destination.Owner).GridUid != home)
            return false;

        if (!CanMove(property))
            return CanTransferDisplay(property, destination, opening: false);

        if (TryComp<HandsComponent>(destination.Owner, out var hands) &&
            _hands.TryGetHand(destination.Owner, destination.ID, out var hand, hands) && hand.Container == destination)
            return true;

        return TryComp<KoronusStationPropertyStorageComponent>(destination.Owner, out var storage) &&
               storage.HomeGrid == home &&
               (TryComp<StorageComponent>(destination.Owner, out var itemStorage) && itemStorage.Container == destination ||
                destination.ID == SharedEntityStorageSystem.ContainerName);
    }

    private bool CanTransferDisplay(KoronusStationPropertyComponent property, BaseContainer container, bool opening)
    {
        SharedEntityStorageComponent? storage = null;
        return TryComp<KoronusStationPropertyStorageComponent>(container.Owner, out var stationStorage) &&
               stationStorage.HomeGrid == property.HomeGrid && Transform(container.Owner).GridUid == property.HomeGrid &&
               _entityStorage.ResolveStorage(container.Owner, ref storage) &&
               storage.Contents == container && storage.Open == opening;
    }

    private void OnPickup(EntityUid uid, KoronusStationPropertyComponent property, GettingPickedUpAttemptEvent args)
    {
        if (!CanMove(property))
            args.Cancel();
    }

    private void OnPull(EntityUid uid, KoronusStationPropertyComponent property, BeingPulledAttemptEvent args)
    {
        if (!CanMove(property))
            args.Cancel();
    }

    private void OnInsert(EntityUid uid, KoronusStationPropertyComponent property, ContainerGettingInsertedAttemptEvent args)
    {
        if (!CanInsert(property, args.Container))
            args.Cancel();
    }

    private void OnRemove(EntityUid uid, KoronusStationPropertyComponent property, ContainerGettingRemovedAttemptEvent args)
    {
        if (!CanMove(property) && !CanTransferDisplay(property, args.Container, opening: true))
            args.Cancel();
    }

    private void OnUse(EntityUid uid, KoronusStationPropertyComponent property, GettingUsedAttemptEvent args)
    {
        if (!CanUse(uid, property))
            args.Cancel();
    }

    private void OnInteract(EntityUid uid, KoronusStationPropertyComponent property, ref GettingInteractedWithAttemptEvent args)
    {
        // Storage must still open. Other display items cannot be activated, dismantled or used as tools.
        if (!CanUse(uid, property) &&
            (CanMove(property) || HasComp<ItemComponent>(uid) && !HasComp<StorageComponent>(uid)))
            args.Cancelled = true;
    }

    private void OnExamined(EntityUid uid, KoronusStationPropertyComponent property, ref ExaminedEvent args)
    {
        if (!IsProperty(uid))
            return;

        args.PushMarkup(Loc.GetString(property.Policy == KoronusStationPropertyPolicy.LocalUse
            ? "koronus-property-examine-local"
            : "koronus-property-examine-display"));
    }
}
