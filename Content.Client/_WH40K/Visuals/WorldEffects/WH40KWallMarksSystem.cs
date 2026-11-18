using System.Numerics;
using Content.Client.IconSmoothing;
using Content.Shared._WH40K.Weapons;
using Content.Shared.CCVar;
using Content.Shared.Particles;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Visuals.WorldEffects;

/// <summary>Short-lived, lit marks on existing wall sprites. No mark entities are created.</summary>
public sealed partial class WH40KWallMarksSystem : EntitySystem
{
    private const int MaxMarks = 96;
    private const int MaxMarksPerWall = 3;
    private static readonly TimeSpan MarkLifetime = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(1);
    private static readonly ResPath Art = new("_WH40K/Effects/wall_impacts.rsi");

    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly List<WallMark> _marks = new(MaxMarks);
    private TimeSpan _nextCleanup;
    private int _nextKey;

    public override void Initialize()
    {
        SubscribeNetworkEvent<WH40KWallMarkEvent>(OnWallMark);
    }

    private void OnWallMark(WH40KWallMarkEvent ev)
    {
        if (_config.GetCVar(CCVars.ParticleQuality) == 0 ||
            !TryGetEntity(ev.Wall, out var wall) || !TryComp<SpriteComponent>(wall, out var sprite))
            return;

        var state = GetMarkState(wall.Value, sprite, ev.Material);
        if (state == null)
            return;

        var onWall = 0;
        var oldestOnWall = -1;
        for (var i = 0; i < _marks.Count; i++)
        {
            var mark = _marks[i];
            if (mark.Wall != wall.Value)
                continue;

            if (Vector2.DistanceSquared(mark.Offset, ev.Offset) < 0.01f)
                return;

            oldestOnWall = oldestOnWall < 0 ? i : oldestOnWall;
            onWall++;
        }

        if (onWall >= MaxMarksPerWall)
            RemoveMark(oldestOnWall);
        else if (_marks.Count >= MaxMarks)
            RemoveMark(0);

        var key = $"wh40k-wall-impact-{_nextKey++}";
        var target = (wall.Value, sprite);
        var layer = _sprites.AddLayer(target, new SpriteSpecifier.Rsi(Art, state));
        _sprites.LayerMapSet(target, key, layer);
        _sprites.LayerSetOffset(target, layer, ev.Offset);
        _sprites.LayerSetRotation(target, layer, ev.Rotation);
        _marks.Add(new WallMark(wall.Value, ev.Offset, key, _timing.CurTime + MarkLifetime));
    }

    private string? GetMarkState(EntityUid wall, SpriteComponent sprite, ParticleSurfaceMaterial material)
    {
        // Wall tiers can share identical damage modifiers. The smoothing family still identifies the cladding.
        var surface = sprite.BaseRSI?.Path.FilenameWithoutExtension ?? string.Empty;
        var smoothing = CompOrNull<IconSmoothComponent>(wall);
        var family = smoothing?.SmoothKey ?? string.Empty;
        var pattern = smoothing?.StateBase ?? string.Empty;
        if (family.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            surface.Contains("glass", StringComparison.OrdinalIgnoreCase) || surface.Contains("window", StringComparison.OrdinalIgnoreCase))
            return null;

        if (material is ParticleSurfaceMaterial.Default or ParticleSurfaceMaterial.Metal)
        {
            if (family == "rockwalls")
                return "stone";
            if (family == "woods" || surface.Contains("wood", StringComparison.OrdinalIgnoreCase))
                return "wood";
            if (surface.Contains("concrete", StringComparison.OrdinalIgnoreCase) || pattern.Contains("brick", StringComparison.OrdinalIgnoreCase))
                return "concrete";
            if (surface.Contains("plastic", StringComparison.OrdinalIgnoreCase))
                return "plastic";
        }

        return material switch
        {
            ParticleSurfaceMaterial.Default or ParticleSurfaceMaterial.Metal => "metal",
            ParticleSurfaceMaterial.Stone => "stone",
            ParticleSurfaceMaterial.Concrete => "concrete",
            ParticleSurfaceMaterial.Wood => "wood",
            ParticleSurfaceMaterial.Plastic => "plastic",
            _ => null,
        };
    }

    public override void FrameUpdate(float frameTime)
    {
        var now = _timing.CurTime;
        if (_marks.Count == 0 || now < _nextCleanup)
            return;

        _nextCleanup = now + CleanupInterval;
        var enabled = _config.GetCVar(CCVars.ParticleQuality) > 0;
        for (var i = _marks.Count - 1; i >= 0; i--)
        {
            var mark = _marks[i];
            if (!enabled || mark.Expires <= now || TerminatingOrDeleted(mark.Wall))
                RemoveMark(i);
        }
    }

    public override void Shutdown()
    {
        for (var i = _marks.Count - 1; i >= 0; i--)
            RemoveMark(i);
    }

    private void RemoveMark(int index)
    {
        var mark = _marks[index];
        if (TryComp<SpriteComponent>(mark.Wall, out var sprite))
            _sprites.RemoveLayer((mark.Wall, sprite), mark.Key, logMissing: false);
        _marks.RemoveAt(index);
    }

    private readonly record struct WallMark(EntityUid Wall, Vector2 Offset, string Key, TimeSpan Expires);
}
