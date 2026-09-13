using System.Linq;
using System.Numerics;
using Content.Client.Stealth;
using Content.Shared.Body.Components;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._WH40K.DroneVision;

public sealed class WH40KDroneVisionOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly TransformSystem _transform;
    private readonly StealthSystem _stealth;
    private readonly ContainerSystem _container;
    private readonly List<DroneVisionRenderEntry> _entries = [];

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WH40KDroneVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _container = _entityManager.System<ContainerSystem>();
        _transform = _entityManager.System<TransformSystem>();
        _stealth = _entityManager.System<StealthSystem>();
        ZIndex = -1;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is null || args.Viewport.Eye is not { } eye || _playerManager.LocalEntity is not { } player)
            return;

        if (!_entityManager.TryGetComponent(player, out TransformComponent? _))
            return;

        var mapId = eye.Position.MapId;
        var eyeRotation = eye.Rotation;
        _entries.Clear();

        var entities = _entityManager.EntityQueryEnumerator<BodyComponent, SpriteComponent, TransformComponent>();
        while (entities.MoveNext(out var uid, out var body, out var sprite, out var transform))
        {
            if (!body.ThermalVisibility || player == uid || !CanSee(uid, sprite))
                continue;

            // A body in a locker, crate, or another container must not be revealed by thermal vision.
            if (_container.TryGetOuterContainer(uid, transform, out _))
                continue;

            if (_entries.Any(entry => entry.Entity.Owner == uid))
                continue;

            _entries.Add(new DroneVisionRenderEntry((uid, sprite, transform), mapId, eyeRotation));
        }

        foreach (var entry in _entries)
        {
            Render(entry.Entity, entry.Map, args.WorldHandle, entry.EyeRotation);
        }

        args.WorldHandle.SetTransform(Matrix3x2.Identity);
    }

    private void Render(Entity<SpriteComponent, TransformComponent> entity, MapId? mapId, DrawingHandleWorld handle, Angle eyeRotation)
    {
        var (uid, sprite, transform) = entity;
        if (transform.MapID != mapId || !CanSee(uid, sprite))
            return;

        var originalColor = sprite.Color;
        sprite.Color = Color.Black;
        sprite.Render(handle, eyeRotation, _transform.GetWorldRotation(transform), position: _transform.GetWorldPosition(transform));
        sprite.Color = originalColor;
    }

    private bool CanSee(EntityUid uid, SpriteComponent sprite)
    {
        return sprite.Visible &&
               (!_entityManager.TryGetComponent(uid, out StealthComponent? stealth) ||
                _stealth.GetVisibility(uid, stealth) > 0.5f);
    }

    private readonly record struct DroneVisionRenderEntry(
        Entity<SpriteComponent, TransformComponent> Entity,
        MapId? Map,
        Angle EyeRotation);
}
