using System.Linq;
using System.Numerics;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Client.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Client._Mono.Shipyard;

/// <summary>
/// This handles spawning client-side grid and getting data from it.
/// </summary>
public sealed partial class ShipyardPreviewSystem : SharedShipyardPreviewSystem
{
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private TransformSystem _xform = default!;

    public Entity<MapGridComponent>? CurrentGrid;

    private VesselPrototype? _pendingVessel;
    private EntityUid? _previewObserver;

    public bool IsPreviewActive => _pendingVessel != null || _previewObserver != null || CurrentGrid != null;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
    }

    /// <summary>
    /// Queues a client-only grid load. The grid itself must only be created after the player has moved to the
    /// preview map, otherwise the sprite tree can observe a partially constructed transform hierarchy.
    /// </summary>
    public bool BeginPreview(VesselPrototype vessel)
    {
        if (IsPreviewActive)
            return false;

        _pendingVessel = vessel;
        return true;
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent args)
    {
        if (!IsPreviewObserver(args.Entity))
            return;

        _previewObserver = args.Entity;
        if (_pendingVessel is not { } vessel)
            return;

        _pendingVessel = null;
        if (!TryLoadPreviewGrid(vessel))
            Log.Error($"Failed to load shipyard preview grid for vessel '{vessel.ID}'.");
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent args)
    {
        if (args.Entity != _previewObserver)
            return;

        _previewObserver = null;
        _pendingVessel = null;
        QueueDisposePreviewGrid();
    }

    private bool TryLoadPreviewGrid(VesselPrototype vessel)
    {
        if (!TryGetPreviewMap(out var previewMap) || !_map.TryGetMap(previewMap, out var mapUid))
        {
            Log.Error("Cannot load shipyard preview grid because the preview map is unavailable on the client.");
            return false;
        }

        var opts = new DeserializationOptions();
        if (!_loader.TryLoadGrid(previewMap,
                vessel.ShuttlePath,
                out var grid,
                opts))
            return false;

        // A preview grid is always a root directly below the preview map. SetCoordinates makes this explicit
        // instead of relying on the transient hierarchy created by the map loader.
        _xform.SetCoordinates(grid.Value.Owner, new EntityCoordinates(mapUid.Value, Vector2.Zero));
        if (!TryComp(grid.Value.Owner, out TransformComponent? xform) ||
            !IsAttachedToPreviewMap(grid.Value, mapUid.Value, previewMap, xform))
        {
            Log.Error($"Shipyard preview grid '{vessel.ID}' was not attached to its preview map.");
            QueueDel(grid.Value.Owner);
            return false;
        }

        _meta.SetEntityName(grid.Value, vessel.Name);
        CurrentGrid = grid.Value;
        return true;
    }

    public bool TryGetCurrentGrid(out Entity<MapGridComponent> grid)
    {
        grid = default;
        if (CurrentGrid is not { } current ||
            !Exists(current.Owner) ||
            TerminatingOrDeleted(current.Owner) ||
            !TryComp<MapGridComponent>(current.Owner, out var gridComponent) ||
            !TryComp(current.Owner, out TransformComponent? xform) ||
            !TryGetPreviewMap(out var previewMap) ||
            !_map.TryGetMap(previewMap, out var mapUid) ||
            !IsAttachedToPreviewMap((current.Owner, gridComponent), mapUid.Value, previewMap, xform))
        {
            QueueDisposePreviewGrid();
            return false;
        }

        grid = (current.Owner, gridComponent);
        return true;
    }

    public FormattedMessage GetGridData()
    {
        var msg = new FormattedMessage();
        if (!TryGetCurrentGrid(out var grid))
            return msg;

        msg.AddMarkupOrThrow(
            Loc.GetString("shipyard-preview-tile-count", ("count", _map.GetAllTiles(grid.Owner, grid.Comp).Count().ToString()))
            );

        return msg;
    }

    private bool IsPreviewObserver(EntityUid entity)
    {
        return TryComp(entity, out MetaDataComponent? metadata) &&
               metadata.EntityPrototype?.ID == "PreviewObserver";
    }

    private static bool IsAttachedToPreviewMap(Entity<MapGridComponent> grid, EntityUid mapUid, MapId mapId, TransformComponent? xform = null)
    {
        return xform != null &&
               xform.ParentUid == mapUid &&
               xform.MapUid == mapUid &&
               xform.MapID == mapId &&
               xform.GridUid == grid.Owner;
    }

    private void QueueDisposePreviewGrid()
    {
        if (CurrentGrid is not { } grid)
            return;

        CurrentGrid = null;
        if (Exists(grid.Owner) && !TerminatingOrDeleted(grid.Owner))
            QueueDel(grid.Owner);
    }
}

