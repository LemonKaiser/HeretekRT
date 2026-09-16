using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Server._NF.Station.Systems;

namespace Content.Server.Warps;

public sealed partial class WarpPointSystem : EntitySystem
{
    [Dependency] private StationRenameWarpsSystems _renameWarps = default!; // Frontier
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WarpPointComponent, ExaminedEvent>(OnWarpPointExamine);
        SubscribeLocalEvent<WarpPointComponent, ComponentStartup>(OnStartup); // Frontier
    }

    private void OnWarpPointExamine(EntityUid uid, WarpPointComponent component, ExaminedEvent args)
    {
        if (!HasComp<GhostComponent>(args.Examiner))
            return;

        var loc = component.Location == null ? "<null>" : $"'{component.Location}'";
        args.PushText(Loc.GetString("warp-point-component-on-examine-success", ("location", loc)));
    }

    // Frontier
    private void OnStartup(EntityUid uid, WarpPointComponent component, ComponentStartup args)
    {
        _renameWarps.SetInitialWarpPointLocation((uid, component));
    }
    // End Frontier
}
