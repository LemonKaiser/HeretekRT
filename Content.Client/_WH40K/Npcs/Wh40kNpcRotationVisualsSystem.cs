using Content.Shared.Rotation;
using Content.Shared.Buckle.Components;
using Content.Shared._WH40K.Npcs;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client._WH40K.Npcs;

/// <summary>Refresh replicated NPC poses and native chair sorting, including actors already seated on arrival.</summary>
public sealed partial class Wh40kNpcRotationVisualsSystem : EntitySystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IEyeManager _eye = default!;
    private readonly Dictionary<EntityUid, Angle> _displayedAngles = new();
    private readonly Dictionary<EntityUid, int> _seatDrawDepths = new();
    private float _timer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kNpcProtectionComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, Wh40kNpcProtectionComponent component, ComponentShutdown args)
    {
        _displayedAngles.Remove(uid);
        RestoreSeatDepth(uid);
    }

    private void RestoreSeatDepth(EntityUid uid)
    {
        if (_seatDrawDepths.Remove(uid, out var depth) && TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.SetDrawDepth((uid, sprite), depth);
    }

    private void UpdateSeatDepth(EntityUid uid)
    {
        if (!TryComp<BuckleComponent>(uid, out var buckle) || buckle.BuckledTo is not { } seat ||
            !TryComp<StrapComponent>(seat, out var strap) || strap.Position != StrapPosition.Stand ||
            !TryComp<SpriteComponent>(seat, out var seatSprite) || !TryComp<SpriteComponent>(uid, out var sprite))
        {
            RestoreSeatDepth(uid);
            return;
        }

        // Server buckling arrives as component state, without the client's predicted BuckledEvent.
        // Match native BuckleSystem sorting and preserve its original depth when it already handled a buckle.
        if (!_seatDrawDepths.TryGetValue(uid, out var original))
        {
            original = buckle.OriginalDrawDepth ?? sprite.DrawDepth;
            _seatDrawDepths.Add(uid, original);
        }
        var screenFacing = _transform.GetWorldRotation(seat) + _eye.CurrentEye.Rotation;
        var depth = screenFacing.GetCardinalDir() == Direction.North ? seatSprite.DrawDepth - 1 : original;
        if (sprite.DrawDepth != depth)
            _sprite.SetDrawDepth((uid, sprite), depth);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 0.1f) return;
        _timer = 0;
        var query = EntityQueryEnumerator<Wh40kNpcProtectionComponent, RotationVisualsComponent, AppearanceComponent>();
        while (query.MoveNext(out var uid, out _, out var rotation, out var appearance))
        {
            UpdateSeatDepth(uid);
            if (_displayedAngles.TryGetValue(uid, out var previous) && previous == rotation.HorizontalRotation)
                continue;
            _displayedAngles[uid] = rotation.HorizontalRotation;
            _appearance.QueueUpdate(uid, appearance);
        }
    }
}
