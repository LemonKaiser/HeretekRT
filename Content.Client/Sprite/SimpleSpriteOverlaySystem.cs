using Content.Shared.Sprite;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.Sprite;

public sealed partial class SimpleSpriteOverlaySystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SimpleSpriteOverlayComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SimpleSpriteOverlayComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
        SubscribeLocalEvent<SimpleSpriteOverlayComponent, ComponentShutdown>(OnCompShutdown);
    }

    private void OnStartup(Entity<SimpleSpriteOverlayComponent> ent, ref ComponentStartup args)
    {
        ApplyOverlay(ent);
    }

    private void OnAfterHandleState(Entity<SimpleSpriteOverlayComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplyOverlay(ent);
    }

    private void ApplyOverlay(Entity<SimpleSpriteOverlayComponent> ent)
    {
        if (ent.Comp.OverlaySprite == SpriteSpecifier.Invalid ||
            !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var index = _sprite.LayerMapReserve((ent.Owner, sprite), ent.Comp.LayerMap);

        _sprite.LayerSetSprite((ent.Owner, sprite), index, ent.Comp.OverlaySprite);
        _sprite.LayerSetVisible((ent.Owner, sprite), index, true);

        if (ent.Comp.Shader is not null)
            sprite.LayerSetShader(index, ent.Comp.Shader);
    }

    private void OnCompShutdown(Entity<SimpleSpriteOverlayComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (_sprite.LayerMapTryGet((ent.Owner, sprite), ent.Comp.LayerMap, out var index, false))
            _sprite.LayerSetVisible((ent.Owner, sprite), index, false);
    }
}
