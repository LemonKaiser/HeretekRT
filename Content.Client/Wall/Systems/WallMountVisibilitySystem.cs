using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared.Tag;
using Content.Shared.Wall;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;

namespace Content.Client.Wall.Systems;

/// <summary>
/// Manages the directional visibility overlay for wall-mounted entities.
/// </summary>
public sealed partial class WallMountVisibilitySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private TransformSystem _xform = default!;

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    private WallMountVisibilityOverlay _overlayInstance = default!;

    /// <summary>
    /// Whether directional visibility is currently enabled.
    /// </summary>
    public bool DirectionalVisibilityEnabled = true;

    /// <summary>
    /// Whether wall-mount visibility changes fade smoothly or snap instantly.
    /// </summary>
    public bool FadeEnabled = true;

    public override void Initialize()
    {
        base.Initialize();

        _overlayInstance = new WallMountVisibilityOverlay();

        Subs.CVar(_cfg, CCVars.WallMountDirectionalVisibility, OnDirectionalVisibilityChanged, true);
        Subs.CVar(_cfg, CCVars.WallMountFade, OnFadeChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay(_overlayInstance);
    }

    private void OnDirectionalVisibilityChanged(bool enabled)
    {
        DirectionalVisibilityEnabled = enabled;

        if (enabled)
            _overlay.AddOverlay(_overlayInstance);
        else
        {
            _overlay.RemoveOverlay(_overlayInstance);
            _overlayInstance.RestoreAll();
        }
    }

    private void OnFadeChanged(bool enabled)
    {
        FadeEnabled = enabled;
    }

    /// <summary>
    /// Makes the entity visible again on component shutdown.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnWallMountShutdown(Entity<WallMountComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!_spriteQuery.TryComp(ent, out var sprite))
            return;

        _sprite.SetVisible((ent, sprite), true);
    }

    /// <summary>
    /// Makes the entity visible again if directional visibility is disabled for this mount.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnWallMountAfterHandleState(Entity<WallMountComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Comp.DirectionalVisibility)
            return;

        if (!_spriteQuery.TryComp(ent, out var sprite))
            return;

        _sprite.SetVisible((ent, sprite), true);
    }

    /// <summary>
    /// Finds the wall supporting a mount, either on its tile or directly behind its facing side.
    /// </summary>
    public bool TryGetWallTile(Entity<MapGridComponent> grid, Vector2i tile, Vector2 facing, out Vector2i wallTile)
    {
        if (HasWall(grid, tile))
        {
            wallTile = tile;
            return true;
        }

        // Mounts can be anchored on the empty tile in front of their wall.
        var localFacing = (-_xform.GetWorldRotation(grid.Owner)).RotateVec(facing);
        var behind = MathF.Abs(localFacing.X) > MathF.Abs(localFacing.Y)
            ? new Vector2i(localFacing.X > 0 ? -1 : 1, 0)
            : new Vector2i(0, localFacing.Y > 0 ? -1 : 1);
        wallTile = tile + behind;
        return HasWall(grid, wallTile);
    }

    private bool HasWall(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var enumerator = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile);
        while (enumerator.MoveNext(out var anchored))
        {
            if (!_tag.HasTag(anchored.Value, "Wall"))
                continue;

            return true;
        }

        return false;
    }
}
