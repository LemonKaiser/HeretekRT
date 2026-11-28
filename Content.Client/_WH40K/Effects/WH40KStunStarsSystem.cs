using System.Numerics;
using Content.Shared.Stunnable;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Effects;

/// <summary>
/// Shows Arcane's animated stars above entities that are stunned or knocked down.
/// </summary>
public sealed partial class WH40KStunStarsSystem : EntitySystem
{
    private const string LayerKey = "WH40KStunStars";
    private static readonly ResPath StarsRsi = new("/Textures/_WH40K/Effects/stunned.rsi");

    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        // Component lifecycle events are exclusive per component type. SharedStunSystem
        // already owns StunnedComponent's startup and shutdown handlers, while it owns
        // KnockedDownComponent's init and shutdown handlers. Subscribe to the unused
        // lifecycle events instead.
        SubscribeLocalEvent<StunnedComponent, ComponentInit>(OnStunnedStarted);
        SubscribeLocalEvent<StunnedComponent, ComponentRemove>(OnStatusEnded);
        SubscribeLocalEvent<KnockedDownComponent, ComponentStartup>(OnKnockdownStarted);
        SubscribeLocalEvent<KnockedDownComponent, ComponentRemove>(OnStatusEnded);
    }

    private void OnStunnedStarted(Entity<StunnedComponent> entity, ref ComponentInit args)
    {
        UpdateStars(entity.Owner);
    }

    private void OnKnockdownStarted(Entity<KnockedDownComponent> entity, ref ComponentStartup args)
    {
        UpdateStars(entity.Owner);
    }

    private void OnStatusEnded<T>(Entity<T> entity, ref ComponentRemove args) where T : IComponent
    {
        Timer.Spawn(0, () =>
        {
            if (Exists(entity.Owner))
                UpdateStars(entity.Owner);
        });
    }

    private void UpdateStars(EntityUid uid)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        var visible = HasComp<StunnedComponent>(uid) || HasComp<KnockedDownComponent>(uid);
        if (!_sprite.LayerMapTryGet((uid, sprite), LayerKey, out var layer, false))
        {
            if (!visible)
                return;

            layer = _sprite.LayerMapReserve((uid, sprite), LayerKey);
            _sprite.LayerSetRsi((uid, sprite), layer, StarsRsi);
            _sprite.LayerSetRsiState((uid, sprite), layer, "stunned");
            _sprite.LayerSetOffset((uid, sprite), layer, new Vector2(0f, 0.3125f));
        }

        _sprite.LayerSetVisible((uid, sprite), layer, visible);
    }
}
