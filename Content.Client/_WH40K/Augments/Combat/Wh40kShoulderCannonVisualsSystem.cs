using Content.Shared._WH40K.Augments.Combat;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Augments.Combat;

public sealed class Wh40kShoulderCannonVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private const string FrontKey = "wh40k-shoulder-cannon";
    private const string FrontGlowKey = "wh40k-shoulder-cannon-glow";
    private const string BackKey = "wh40k-shoulder-cannon-back";
    private const string BackGlowKey = "wh40k-shoulder-cannon-back-glow";
    private const string HelmetKey = "head";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kShoulderCannonDeployedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kShoulderCannonDeployedComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<Wh40kShoulderCannonDeployedComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<Wh40kShoulderCannonDeployedComponent> ent, ref ComponentStartup args)
        => UpdateLayers(ent);

    private void OnState(Entity<Wh40kShoulderCannonDeployedComponent> ent, ref AfterAutoHandleStateEvent args)
        => UpdateLayers(ent);

    private void OnShutdown(Entity<Wh40kShoulderCannonDeployedComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        foreach (var key in new[] { FrontKey, FrontGlowKey, BackKey, BackGlowKey })
        {
            if (_sprite.LayerMapTryGet((ent, sprite), key, out var layer, false))
                _sprite.LayerSetVisible((ent, sprite), layer, false);
        }
    }

    private void UpdateLayers(Entity<Wh40kShoulderCannonDeployedComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        Entity<SpriteComponent> target = (ent, sprite);
        var comp = ent.Comp;
        Ensure(target, FrontKey, comp.State, () =>
            _sprite.LayerMapTryGet(target.AsNullable(), HelmetKey, out var helmet, false) ? helmet : null);
        Ensure(target, FrontGlowKey, comp.GlowState, () => _sprite.LayerMapGet(target.AsNullable(), FrontKey) + 1);
        Ensure(target, BackKey, comp.State + "-back", () => 0);
        Ensure(target, BackGlowKey, comp.GlowState + "-back", () => _sprite.LayerMapGet(target.AsNullable(), BackKey) + 1);

        Set(FrontKey, comp.State, true, false);
        Set(BackKey, comp.State + "-back", true, false);
        Set(FrontGlowKey, comp.GlowState, comp.Charging, true);
        Set(BackGlowKey, comp.GlowState + "-back", comp.Charging, true);

        void Set(string key, string state, bool visible, bool unshaded)
        {
            var layer = _sprite.LayerMapGet(target.AsNullable(), key);
            _sprite.LayerSetSprite(target.AsNullable(), layer, new SpriteSpecifier.Rsi(comp.Rsi, state));
            _sprite.LayerSetVisible(target.AsNullable(), layer, visible);
            if (unshaded)
                sprite.LayerSetShader(layer, "unshaded");
        }
    }

    private void Ensure(Entity<SpriteComponent> sprite, string key, string state, Func<int?> index)
    {
        if (_sprite.LayerMapTryGet(sprite.AsNullable(), key, out _, false))
            return;

        var rsi = Comp<Wh40kShoulderCannonDeployedComponent>(sprite).Rsi;
        var layer = _sprite.AddLayer(sprite.AsNullable(), new SpriteSpecifier.Rsi(rsi, state), index());
        _sprite.LayerMapSet(sprite.AsNullable(), key, layer);
    }
}
