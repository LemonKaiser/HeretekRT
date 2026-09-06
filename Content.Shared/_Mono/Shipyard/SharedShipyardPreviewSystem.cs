
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Mono.Shipyard;

public abstract partial class SharedShipyardPreviewSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _mapManager = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    protected MapId _previewMap = MapId.Nullspace;
    public override void Initialize()
    {

    }

    public bool TryPreviewEntity(EntityUid player)
    {
        if (_mind.GetMind(player) is not { } mind)
            return false;

        if (!TryComp<MindComponent>(mind, out var mindComp) || mindComp.VisitingEntity != null)
            return false;

        if (!TryGetPreviewMap(out var previewMap))
            return false;

        var observer = Spawn("PreviewObserver", new MapCoordinates(0, 0, previewMap));

        _mind.Visit(mind, observer);

        return true;
    }

    public bool TryGetPreviewMap(out MapId previewMap)
    {
        CachePreviewMap();

        previewMap = _previewMap;
        return previewMap != MapId.Nullspace && _mapManager.MapExists(previewMap);
    }

    public void CachePreviewMap()
    {
        if (_previewMap != MapId.Nullspace && _mapManager.MapExists(_previewMap))
            return;

        _previewMap = MapId.Nullspace;
        var eQe = AllEntityQuery<PreviewMapComponent, MapComponent>();

        while (eQe.MoveNext(out var map, out _, out var comp))
        {
            _previewMap = comp.MapId;
        }
    }
}
