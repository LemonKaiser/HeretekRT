using Content.Shared.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Follower;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Enums;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class FollowCommand : IConsoleCommand
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public string Command => "follow";
    public string Description => Loc.GetString("follow-command-description");
    public string Help => Loc.GetString("follow-command-help");

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-need-exactly-one-argument"));
            return;
        }

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { Valid: true } playerEntity)
        {
            shell.WriteError(Loc.GetString("shell-must-be-attached-to-entity"));
            return;
        }

        var entity = args[0];
        if (NetEntity.TryParse(entity, out var uidNet) && _entManager.TryGetEntity(uidNet, out var uid))
        {
            if (_playerManager.TryGetSessionByEntity(uid.Value, out var targetSession)
                && await _adminActionGuard.TryDenyProtectedTargetAsync(
                    player,
                    targetSession.UserId,
                    Loc.GetString("admin-hierarchy-action-follow"),
                    targetSession.Name,
                    shell.WriteError))
            {
                return;
            }

            _entManager.System<FollowerSystem>().StartFollowingEntity(playerEntity, uid.Value);
        }
    }
}
