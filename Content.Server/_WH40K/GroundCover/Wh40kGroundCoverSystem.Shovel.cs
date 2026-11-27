using Content.Server.Digging;
using Content.Shared._WH40K.GroundCover;
using Content.Shared.Burial.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.GroundCover;

public sealed partial class Wh40kGroundCoverSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private void InitializeShovel()
    {
        // Snow comes off before the ground under it is dug.
        SubscribeLocalEvent<ShovelComponent, AfterInteractEvent>(OnShovelInteract,
            before: new[] { typeof(SharedToolSystem), typeof(DiggingSystem) });
        SubscribeLocalEvent<ShovelComponent, Wh40kSnowShovelDoAfterEvent>(OnShoveled);
    }

    private void OnShovelInteract(EntityUid uid, ShovelComponent shovel, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target != null)
            return;

        var click = _transform.ToMapCoordinates(args.ClickLocation);
        if (!_map.TryFindGridAt(click, out var gridUid, out var grid))
            return;

        var tile = _map.TileIndicesFor(gridUid, grid, click);
        if (GetDepth(gridUid, tile) == 0
            || !_interaction.InRangeUnobstructed(args.User, _map.GridTileToLocal(gridUid, grid, tile), popup: false))
            return;

        var speed = TryComp<ToolComponent>(uid, out var tool) ? tool.SpeedModifier : shovel.SpeedModifier;
        var clear = new Wh40kSnowShovelDoAfterEvent(GetNetEntity(gridUid), tile);
        var doAfter = new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(0.75f / MathF.Max(speed, 0.05f)),
            tool != null ? new SharedToolSystem.ToolDoAfterEvent(0f, clear, GetNetEntity(uid)) : clear, uid, target: uid, used: uid)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        args.Handled = true;
    }

    private void OnShoveled(EntityUid uid, ShovelComponent shovel, Wh40kSnowShovelDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!TryGetEntity(args.Grid, out var grid)
            || !TryComp<MapGridComponent>(grid, out var gridComp)
            || GetDepth(grid.Value, args.Tile) == 0
            || !_interaction.InRangeUnobstructed(args.User, _map.GridTileToLocal(grid.Value, gridComp, args.Tile), popup: false))
            return;

        SetDepth(grid.Value, args.Tile, 0);
        _popup.PopupEntity(Loc.GetString("wh40k-snow-cover-shoveled"), args.User, args.User);
    }
}
