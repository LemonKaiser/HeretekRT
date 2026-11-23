using Content.Shared._White.Standing;
using Content.Shared.CCVar;
using Content.Shared.Stunnable;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Standing;

/// <summary>
/// Decides whether a knocked-down body gets up by itself: its own player's setting, or always when it has no player.
/// </summary>
public sealed partial class Wh40kAutoGetUpSystem : EntitySystem
{
    [Dependency] private INetConfigurationManager _cfg = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KnockedDownComponent, ComponentStartup>(OnKnockedDown);
        SubscribeLocalEvent<LayingDownComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LayingDownComponent, PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnKnockedDown(Entity<KnockedDownComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp(ent, out LayingDownComponent? layingDown))
            return;

        _player.TryGetSessionByEntity(ent, out var session);
        SetFor((ent, layingDown), session);
    }

    // A player who takes or leaves a body part way through a knockdown counts from then on.
    private void OnPlayerAttached(EntityUid uid, LayingDownComponent component, PlayerAttachedEvent args)
    {
        SetFor((uid, component), args.Player);
    }

    private void OnPlayerDetached(EntityUid uid, LayingDownComponent component, PlayerDetachedEvent args)
    {
        SetFor((uid, component), null);
    }

    /// <summary>
    /// Writes the player's setting on the body, or always when nobody is in it.
    /// </summary>
    private void SetFor(Entity<LayingDownComponent> ent, ICommonSession? player)
    {
        var auto = player == null || _cfg.GetClientCVar(player.Channel, CCVars.AutoGetUp);
        if (ent.Comp.AutoGetUp == auto)
            return;

        ent.Comp.AutoGetUp = auto;
        Dirty(ent);
    }
}
