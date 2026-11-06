
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Mono.Shipyard;

public abstract class SharedShipyardPreviewSystem : EntitySystem
{
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    protected MapId _previewMap = MapId.Nullspace;
    public bool TryPreviewEntity(EntityUid player, out EntityUid observer)
    {
        observer = default;
        if (!CachePreviewMap()
            || _mind.GetMind(player) is not { } mind
            || Comp<MindComponent>(mind).VisitingEntity != null
            || HasComp<GhostComponent>(player))
            return false;

        observer = Spawn("PreviewObserver", new MapCoordinates(0, 0, _previewMap));

        _mind.Visit(mind, observer);

        return true;
    }

    public bool CachePreviewMap()
    {
        if (_previewMap != MapId.Nullspace && _mapManager.MapExists(_previewMap))
            return true;

        _previewMap = MapId.Nullspace;

        var eQe = AllEntityQuery<PreviewMapComponent, MapComponent>();

        while (eQe.MoveNext(out var map, out _, out var comp))
        {
            _previewMap = comp.MapId;
            return true;
        }

        return false;
    }
}
