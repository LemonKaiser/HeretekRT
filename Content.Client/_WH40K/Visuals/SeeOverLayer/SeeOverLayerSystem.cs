using Content.Shared._WH40K.Visuals.SeeOverLayer;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._WH40K.Visuals.SeeOverLayer;

/// <summary>
/// Keeps client-only draw-depth overrides in sync with the local player.
/// </summary>
public sealed class SeeOverLayerSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SeeOverLayerVisualsComponent, ComponentStartup>(OnVisualsStartup);
        SubscribeLocalEvent<SeeOverLayerVisualsComponent, ComponentShutdown>(OnVisualsShutdown);
        SubscribeLocalEvent<SeeOverLayerVisualsComponent, AppearanceChangeEvent>(OnAppearanceChanged);
        SubscribeLocalEvent<SeeOverLayerComponent, ComponentStartup>(OnViewerStartup);
        SubscribeLocalEvent<SeeOverLayerComponent, ComponentShutdown>(OnViewerShutdown);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerChanged);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
    }

    private void OnVisualsStartup(Entity<SeeOverLayerVisualsComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
            UpdateDepth((ent.Owner, sprite), ent.Comp, GetLocalLayers());
    }

    private void OnVisualsShutdown(Entity<SeeOverLayerVisualsComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprites.SetDrawDepth((ent.Owner, sprite), ent.Comp.NormalDrawDepth);
    }

    private void OnAppearanceChanged(Entity<SeeOverLayerVisualsComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite != null)
            UpdateDepth((ent.Owner, args.Sprite), ent.Comp, GetLocalLayers());
    }

    private void OnViewerStartup(Entity<SeeOverLayerComponent> ent, ref ComponentStartup args)
    {
        if (_player.LocalEntity == ent.Owner)
            RefreshAll(GetLocalLayers());
    }

    private void OnViewerShutdown(Entity<SeeOverLayerComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity == ent.Owner)
            RefreshAll(GetLocalLayers());
    }

    private void OnLocalPlayerChanged(LocalPlayerAttachedEvent args)
    {
        RefreshAll(GetLocalLayers());
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent args)
    {
        RefreshAll([]);
    }

    private HashSet<string> GetLocalLayers()
    {
        return _player.LocalEntity is { } local && TryComp<SeeOverLayerComponent>(local, out var component)
            ? component.Layers
            : [];
    }

    private void RefreshAll(HashSet<string> layers)
    {
        var query = EntityQueryEnumerator<SeeOverLayerVisualsComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var sprite))
        {
            UpdateDepth((uid, sprite), visuals, layers);
        }
    }

    private void UpdateDepth(Entity<SpriteComponent> entity, SeeOverLayerVisualsComponent visuals, HashSet<string> layers)
    {
        _sprites.SetDrawDepth((entity.Owner, (SpriteComponent?) entity.Comp),
            layers.Contains(visuals.Layer) ? visuals.SeeOverDrawDepth : visuals.NormalDrawDepth);
    }
}
