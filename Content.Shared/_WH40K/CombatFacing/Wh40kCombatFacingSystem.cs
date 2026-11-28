using Content.Shared.CombatMode;
using Content.Shared.MouseRotator;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.CombatFacing;

/// <summary>Applies per-player combat cursor-facing preference, including mid-combat toggles.</summary>
public sealed partial class Wh40kCombatFacingSystem : EntitySystem
{
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private INetConfigurationManager _netConfig = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<Wh40kCombatFaceCursorChangedEvent>(OnChanged);

        if (_net.IsClient)
            Subs.CVar(_netConfig, Wh40kCombatFacingCVars.FaceCursor, OnLocalChanged);
        else
            SubscribeLocalEvent<CombatModeComponent, PlayerAttachedEvent>(OnPlayerAttached);
    }

    public bool WantsFaceCursor(EntityUid uid)
    {
        return !_player.TryGetSessionByEntity(uid, out var session) || WantsFaceCursor(session);
    }

    private bool WantsFaceCursor(ICommonSession session)
    {
        if (_net.IsClient)
            return session != _player.LocalSession || _netConfig.GetCVar(Wh40kCombatFacingCVars.FaceCursor);

        return _netConfig.GetClientCVar(session.Channel, Wh40kCombatFacingCVars.FaceCursor);
    }

    private void OnPlayerAttached(EntityUid uid, CombatModeComponent combat, PlayerAttachedEvent args)
    {
        if (combat.IsInCombatMode && HasComp<MouseRotatorComponent>(uid) && !WantsFaceCursor(args.Player))
            _combat.Wh40kSetFaceCursor(uid, false);
    }

    private void OnLocalChanged(bool faceCursor)
    {
        if (_player.LocalEntity != null)
            RaiseNetworkEvent(new Wh40kCombatFaceCursorChangedEvent(faceCursor));
    }

    private void OnChanged(Wh40kCombatFaceCursorChangedEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } uid)
            _combat.Wh40kSetFaceCursor(uid, ev.FaceCursor);
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kCombatFaceCursorChangedEvent(bool faceCursor) : EntityEventArgs
{
    public readonly bool FaceCursor = faceCursor;
}
