using System;
using Content.Server.GameTicking;
using Content.Server.Ghost;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Content.Shared.GhostTypes;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared._WH40K.MetaProgress;
using Robust.Server.Player;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._WH40K.MetaProgress;

public sealed partial class WH40KGhostDecorationSystem : EntitySystem
{
    private const string DefaultGhostRsiPath = "/Textures/Mobs/Ghosts/ghost_human.rsi";
    private const string DefaultGhostState = "animated";

    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private GhostSpriteStateSystem _ghostSprites = default!;
    [Dependency] private GhostSystem _ghosts = default!;
    [Dependency] private WH40KDecorationSystem _decorations = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private IConfigurationManager _configuration = default!;

    public override void Initialize()
    {
        base.Initialize();
        _ghosts.GhostMindAdded += OnGhostMindAdded;
        _decorations.SelectionChanged += OnSelectionChanged;
        _configuration.OnValueChanged(CCVars.Wh40kDecorationsAdminVisualPriority, OnAdminPriorityChanged, true);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    public override void Shutdown()
    {
        _ghosts.GhostMindAdded -= OnGhostMindAdded;
        _decorations.SelectionChanged -= OnSelectionChanged;
        _configuration.UnsubValueChanged(CCVars.Wh40kDecorationsAdminVisualPriority, OnAdminPriorityChanged);
        base.Shutdown();
    }

    private void OnGhostMindAdded(Entity<GhostComponent> ghost, MindAddedMessage args)
    {
        if (args.Mind.Comp.UserId is not { } userId || !IsDecoratableGhost(ghost.Owner))
        {
            RemoveVisual(ghost.Owner);
            return;
        }

        ApplyDecoration(ghost, userId);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        // AGhost visits the temporary AdminObserver instead of putting the mind into
        // its MindContainer. That path never raises GhostMindAdded, so apply the
        // decoration when the session is actually attached to the admin ghost.
        if (!TryComp<GhostComponent>(args.Entity, out var ghost) || !IsAdminObserverGhost(args.Entity))
            return;

        ApplyDecoration((args.Entity, ghost), args.Player.UserId);
    }

    private void OnSelectionChanged(NetUserId userId)
    {
        if (!_players.TryGetSessionById(userId, out var session) ||
            session.AttachedEntity is not { } entity ||
            !TryComp<GhostComponent>(entity, out var ghost) ||
            !IsDecoratableGhost(entity))
        {
            return;
        }

        ApplyDecoration((entity, ghost), userId);
    }

    private void OnAdminPriorityChanged(bool _)
    {
        var query = EntityQueryEnumerator<GhostComponent>();
        while (query.MoveNext(out var uid, out var ghost))
        {
            if (!IsDecoratableGhost(uid) ||
                !_minds.TryGetMind(uid, out var mindEntity, out MindComponent? mind) ||
                mind?.UserId is not { } userId)
            {
                continue;
            }

            ApplyDecoration((uid, ghost), userId);
        }
    }

    private void ApplyDecoration(Entity<GhostComponent> ghost, NetUserId userId)
    {
        if (!_decorations.TryGetSelectedDecoration(userId, WH40KMetaDecorationCategory.GhostSkins, out var decoration) ||
            decoration == null)
        {
            ClearDecoration(ghost);
            return;
        }

        if (_configuration.GetCVar(CCVars.Wh40kDecorationsAdminVisualPriority) && ghost.Comp.CanGhostInteract)
        {
            // The staff colour may still have priority, but it must not replace the selected skin.
            _ghosts.RestoreObserverGhostColor(ghost);
        }
        else
        {
            var tint = Color.TryFromHex(decoration.GhostTintHex, out var parsedTint) ? parsedTint : Color.White;
            _ghosts.SetGhostDecorationColor(ghost, tint);
        }

        ApplyVisual(ghost.Owner, decoration);
    }

    private void ApplyVisual(EntityUid uid, WH40KMetaDecorationPrototype decoration)
    {
        var rsiPath = string.IsNullOrWhiteSpace(decoration.GhostRsiPath)
            ? DefaultGhostRsiPath
            : decoration.GhostRsiPath!;
        var state = string.IsNullOrWhiteSpace(decoration.GhostState)
            ? DefaultGhostState
            : decoration.GhostState!;

        // A default skin must remove the decoration component.  Keeping it with the
        // default RSI only changed tint on clients and left the previous custom sprite.
        if (string.Equals(rsiPath, DefaultGhostRsiPath, StringComparison.Ordinal))
        {
            RestoreDefaultVisual(uid);
            return;
        }

        _appearance.RemoveData(uid, GhostVisuals.Damage);

        var visual = EnsureComp<WH40KGhostDecorationVisualComponent>(uid);
        if (visual.GhostRsiPath == rsiPath && visual.GhostState == state &&
            visual.GhostTintHex == decoration.GhostTintHex)
            return;

        visual.GhostRsiPath = rsiPath;
        visual.GhostState = state;
        visual.GhostTintHex = decoration.GhostTintHex!;
        Dirty(uid, visual);
    }

    private void ClearDecoration(Entity<GhostComponent> ghost)
    {
        _ghosts.RestoreObserverGhostColor(ghost);
        RestoreDefaultVisual(ghost.Owner);
    }

    private void RemoveVisual(EntityUid uid)
    {
        RestoreDefaultVisual(uid);
    }

    private void RestoreDefaultVisual(EntityUid uid)
    {
        RemCompDeferred<WH40KGhostDecorationVisualComponent>(uid);
        if (TryComp<GhostSpriteStateComponent>(uid, out var spriteState) &&
            _minds.TryGetMind(uid, out var mind, out MindComponent? mindComponent))
        {
            _ghostSprites.SetGhostSprite((uid, spriteState), mind);
        }
    }

    private bool IsDecoratableGhost(EntityUid uid)
    {
        return TryComp<MetaDataComponent>(uid, out var metadata) &&
               metadata.EntityPrototype?.ID is GameTicker.ObserverPrototypeName or GameTicker.AdminObserverPrototypeName;
    }

    private bool IsAdminObserverGhost(EntityUid uid)
    {
        return TryComp<MetaDataComponent>(uid, out var metadata) &&
               metadata.EntityPrototype?.ID == GameTicker.AdminObserverPrototypeName;
    }
}
