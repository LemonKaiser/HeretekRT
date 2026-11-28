using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._WH40K.Camera;

/// <summary>Reads whether the player controlling an entity wants the view turned north on entering a grid.</summary>
public sealed partial class Wh40kDefaultNorthSystem : EntitySystem
{
    [Dependency] private INetConfigurationManager _netConfig = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    public bool WantsNorth(EntityUid uid)
    {
        if (!_player.TryGetSessionByEntity(uid, out var session))
            return false;

        if (_net.IsClient)
            return session == _player.LocalSession && _netConfig.GetCVar(Wh40kCameraCVars.DefaultNorth);

        return _netConfig.GetClientCVar(session.Channel, Wh40kCameraCVars.DefaultNorth);
    }
}
