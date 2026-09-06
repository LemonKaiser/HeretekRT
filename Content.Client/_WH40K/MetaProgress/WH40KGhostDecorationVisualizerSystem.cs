using System;
using System.Collections.Generic;
using Content.Shared._WH40K.MetaProgress;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.MetaProgress;

public sealed class WH40KGhostDecorationVisualizerSystem : EntitySystem
{
    private const string DefaultGhostRsiPath = "/Textures/Mobs/Ghosts/ghost_human.rsi";
    private const string DefaultGhostState = "animated";

    [Dependency] private readonly SpriteSystem _sprite = default!;
    private readonly HashSet<string> _failedSprites = new(StringComparer.Ordinal);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KGhostDecorationVisualComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KGhostDecorationVisualComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WH40KGhostDecorationVisualComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<WH40KGhostDecorationVisualComponent> entity, ref ComponentStartup args)
    {
        Apply(entity);
    }

    private void OnState(Entity<WH40KGhostDecorationVisualComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        Apply(entity);
    }

    private void OnShutdown(Entity<WH40KGhostDecorationVisualComponent> entity, ref ComponentShutdown args)
    {
        ApplySprite(entity.Owner, DefaultGhostRsiPath, DefaultGhostState);
    }

    private void Apply(Entity<WH40KGhostDecorationVisualComponent> entity)
    {
        var path = string.IsNullOrWhiteSpace(entity.Comp.GhostRsiPath)
            ? DefaultGhostRsiPath
            : entity.Comp.GhostRsiPath;
        var state = string.IsNullOrWhiteSpace(entity.Comp.GhostState)
            ? DefaultGhostState
            : entity.Comp.GhostState;

        if (!ApplySprite(entity.Owner, path, state))
            ApplySprite(entity.Owner, DefaultGhostRsiPath, DefaultGhostState);

        ApplyTint(entity.Owner, entity.Comp.GhostTintHex);
    }

    private bool ApplySprite(EntityUid uid, string rsiPath, string state)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return false;

        var spriteId = $"{rsiPath}:{state}";
        if (_failedSprites.Contains(spriteId))
            return false;

        try
        {
            _sprite.LayerSetSprite((uid, sprite), 0, new SpriteSpecifier.Rsi(new ResPath(rsiPath), state));
            return true;
        }
        catch (Exception exception)
        {
            if (_failedSprites.Add(spriteId))
                Log.Warning($"Failed to apply WH40K ghost decoration {rsiPath}:{state} to {ToPrettyString(uid)}: {exception}");
            return false;
        }
    }

    private void ApplyTint(EntityUid uid, string tintHex)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        _sprite.LayerSetColor((uid, sprite), 0, Color.TryFromHex(tintHex, out var tint) ? tint : Color.White);
    }
}
