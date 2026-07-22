using System;
using Content.Server.Administration.Managers;
using Content.Server._WH40K.DeathTransition;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Robust.Shared.Console;

namespace Content.Server.GameTicking.Commands
{
    [AnyCommand]
    sealed partial class ObserveCommand : IConsoleCommand
    {
        [Dependency] private IEntityManager _e = default!;
        [Dependency] private IAdminManager _adminManager = default!;

        public string Command => "observe";
        public string Description => "";
        public string Help => "";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (shell.Player is not { } player)
            {
                shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
                return;
            }

            var ticker = _e.System<GameTicker>();

            if (ticker.RunLevel == GameRunLevel.PreRoundLobby)
            {
                shell.WriteError("Wait until the round starts.");
                return;
            }

            var policy = _e.System<GhostPermissionSystem>();
            var isAdminCommand = args.Length > 0 && args[0].Equals("admin", StringComparison.OrdinalIgnoreCase);

            if (isAdminCommand && !policy.IsGhostStaff(player))
            {
                shell.WriteError("Only active administrators and moderators may observe as staff.");
                return;
            }

            if (!ticker.PlayerGameStatuses.TryGetValue(player.UserId, out var status)
                || status == PlayerGameStatus.JoinedGame)
            {
                shell.WriteError($"{player.Name} is not in the lobby.   This incident will be reported.");
                return;
            }

            if (!isAdminCommand)
            {
                if (policy.IsGhostStaff(player))
                {
                    policy.AuthorizeStaffObserverAsPlayer(player);
                    _adminManager.DeAdmin(player);
                }
                else if (!policy.TryReservePermissionUse(player))
                {
                    shell.WriteError("You do not have an active permission to observe.");
                    return;
                }
            }

            ticker.JoinAsObserver(player);
        }
    }
}
