using System.Numerics;
using Content.Shared._WH40K.ClassProgression;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.ClassProgression;

/// <summary>
/// Client-only presentation of the Soldier's personal priority mark. The server sends it solely to its owner;
/// nothing is replicated onto the marked entity itself.
/// </summary>
public sealed class Wh40kClassTargetMarkVisualizerSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private Wh40kClassTargetMarkOverlay _overlay = default!;
    private EntityUid? _target;
    private TimeSpan _expiresAt;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new Wh40kClassTargetMarkOverlay(this);
        _overlays.AddOverlay(_overlay);
        SubscribeNetworkEvent<Wh40kClassTargetMarkVisualEvent>(OnTargetMarkVisual);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_overlay);
        _target = null;
        base.Shutdown();
    }

    private void OnTargetMarkVisual(Wh40kClassTargetMarkVisualEvent args)
    {
        if (args.Clear)
        {
            _target = null;
            return;
        }

        _target = GetEntity(args.Target);
        _expiresAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0f, args.Duration));
    }

    internal bool TryGetVisibleTarget(MapId mapId, out Vector2 position)
    {
        position = default;
        if (_target is not { } target || _expiresAt <= _timing.CurTime || !Exists(target))
            return false;

        var transform = Transform(target);
        if (transform.MapID != mapId)
            return false;

        position = _transform.GetWorldPosition(transform);
        return true;
    }
}

internal sealed class Wh40kClassTargetMarkOverlay : Overlay
{
    private readonly Wh40kClassTargetMarkVisualizerSystem _system;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public Wh40kClassTargetMarkOverlay(Wh40kClassTargetMarkVisualizerSystem system)
    {
        _system = system;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _system.TryGetVisibleTarget(args.MapId, out _);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_system.TryGetVisibleTarget(args.MapId, out var center))
            return;

        const float radius = 0.42f;
        const float inner = 0.18f;
        var color = Color.FromHex("#E14242").WithAlpha(0.95f);
        var handle = args.WorldHandle;
        handle.DrawLine(center + new Vector2(-radius, 0f), center + new Vector2(-inner, 0f), color);
        handle.DrawLine(center + new Vector2(inner, 0f), center + new Vector2(radius, 0f), color);
        handle.DrawLine(center + new Vector2(0f, -radius), center + new Vector2(0f, -inner), color);
        handle.DrawLine(center + new Vector2(0f, inner), center + new Vector2(0f, radius), color);
    }
}
