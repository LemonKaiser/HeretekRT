using System.Numerics;
using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stealth.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._WH40K.Augments.Combat;

/// <summary>
/// Tactical smartlink sight overlay: displays active weapon round count next to the cursor
/// and brackets + distance in meters around the nearest living uncloaked mob within 12 tiles in line of sight.
/// </summary>
public sealed partial class Wh40kSmartlinkOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resources = default!;

    private readonly Wh40kSmartlinkSystem _smartlink;
    private readonly SharedHandsSystem _hands;
    private readonly SharedTransformSystem _transform;
    private readonly EntityLookupSystem _lookup;
    private readonly ExamineSystemShared _examine;
    private readonly MobStateSystem _mobState;
    private readonly Font _font;
    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    public static readonly Color SightColor = new(0.35f, 0.95f, 1f, 0.9f);
    public static readonly Color LowColor = new(1f, 0.75f, 0.2f, 0.95f);
    public static readonly Color EmptyColor = new(1f, 0.3f, 0.25f, 0.95f);
    private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.75f);

    private const float Range = 12f;
    private const float BoxRadius = 0.6f;
    private const float BracketShare = 0.35f;

    private EntityUid _gun;
    private EntityUid? _target;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public Wh40kSmartlinkOverlay()
    {
        IoCManager.InjectDependencies(this);
        _smartlink = _entities.System<Wh40kSmartlinkSystem>();
        _hands = _entities.System<SharedHandsSystem>();
        _transform = _entities.System<SharedTransformSystem>();
        _lookup = _entities.System<EntityLookupSystem>();
        _examine = _entities.System<ExamineSystemShared>();
        _mobState = _entities.System<MobStateSystem>();
        _font = new VectorFont(_resources.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 10);
        ZIndex = 5;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player ||
            !_entities.TryGetComponent<EyeComponent>(player, out var eye) ||
            eye.Eye == null ||
            args.Viewport.Eye != eye.Eye ||
            !_entities.TryGetComponent<HandsComponent>(player, out var hands) ||
            _hands.GetActiveItem((player, hands)) is not { } gun ||
            !_entities.HasComponent<GunComponent>(gun) ||
            _smartlink.LinkFor(player, gun) is not { } link ||
            _smartlink.IsScrambled(link))
        {
            return false;
        }

        _gun = gun;
        var cursor = _eye.PixelToMap(_input.MouseScreenPosition);
        _target = cursor.MapId == args.MapId ? Nearest(player, cursor) : null;
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var scale = MathF.Min((args.ViewportControl as Control)?.UIScale ?? 1f, 1.25f);

        if (_target is { } target)
            DrawBrackets(handle, target, scale);

        var mouse = _input.MouseScreenPosition;
        if (_eye.PixelToMap(mouse).MapId == args.MapId)
            DrawAmmo(handle, mouse.Position, scale);
    }

    private EntityUid? Nearest(EntityUid player, MapCoordinates cursor)
    {
        _mobs.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(player), Range, _mobs, LookupFlags.Uncontained);

        EntityUid? nearest = null;
        var best = float.MaxValue;
        foreach (var mob in _mobs)
        {
            var distance = (_transform.GetMapCoordinates(mob).Position - cursor.Position).LengthSquared();
            if (distance >= best ||
                mob.Owner == player ||
                _mobState.IsDead(mob, mob.Comp) ||
                (_entities.TryGetComponent<StealthComponent>(mob, out var stealth) && stealth.Enabled) ||
                !_examine.InRangeUnOccluded(player, mob, Range))
            {
                continue;
            }

            best = distance;
            nearest = mob;
        }

        return nearest;
    }

    private void DrawBrackets(DrawingHandleScreen handle, EntityUid target, float scale)
    {
        var world = _transform.GetWorldPosition(target);
        var centre = _eye.WorldToScreen(world);
        var half = MathF.Max((_eye.WorldToScreen(world + new Vector2(BoxRadius, 0f)) - centre).Length(), 10f * scale);
        var arm = half * 2f * BracketShare;
        var width = MathF.Max(2f * scale, 1f);

        foreach (var (x, y) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var corner = centre + new Vector2(x * half, y * half);
            var across = UIBox2.FromDimensions(
                new Vector2(x < 0 ? corner.X : corner.X - arm, y < 0 ? corner.Y : corner.Y - width),
                new Vector2(arm, width));
            var down = UIBox2.FromDimensions(
                new Vector2(x < 0 ? corner.X : corner.X - width, y < 0 ? corner.Y : corner.Y - arm),
                new Vector2(width, arm));
            handle.DrawRect(across.Translated(new Vector2(1f)), ShadowColor);
            handle.DrawRect(down.Translated(new Vector2(1f)), ShadowColor);
            handle.DrawRect(across, SightColor);
            handle.DrawRect(down, SightColor);
        }

        var origin = _player.LocalEntity is { } player ? _transform.GetWorldPosition(player) : world;
        var range = Loc.GetString("wh40k-smartlink-range", ("meters", (int) MathF.Round((world - origin).Length())));
        var size = handle.GetDimensions(_font, range, scale);
        DrawText(handle, centre + new Vector2(-size.X / 2f, half + 3f * scale), range, scale, SightColor);
    }

    private void DrawAmmo(DrawingHandleScreen handle, Vector2 mouse, float scale)
    {
        var count = new GetAmmoCountEvent();
        _entities.EventBus.RaiseLocalEvent(_gun, ref count);
        if (count.Capacity <= 0)
            return;

        var color = count.Count == 0 ? EmptyColor : count.Count * 4 <= count.Capacity ? LowColor : SightColor;
        var text = Loc.GetString("wh40k-smartlink-ammo", ("count", count.Count), ("capacity", count.Capacity));
        DrawText(handle, mouse + new Vector2(16f, 10f) * scale, text, scale, color);
    }

    private void DrawText(DrawingHandleScreen handle, Vector2 position, string text, float scale, Color color)
    {
        handle.DrawString(_font, position + new Vector2(scale), text, scale, ShadowColor);
        handle.DrawString(_font, position, text, scale, color);
    }
}
