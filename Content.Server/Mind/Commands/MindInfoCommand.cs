using System.Text;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server.Mind.Commands
{
    [AdminCommand(AdminFlags.Admin)]
    public sealed partial class MindInfoCommand : IConsoleCommand
    {
        [Dependency] private IEntityManager _entities = default!;
        [Dependency] private IPlayerManager _playerManager = default!;
        [Dependency] private Content.Server.Administration.Managers.IAdminAuthorizationManager _authorization = default!;

        public string Command => "mindinfo";
        public string Description => "Lists info for the mind of a specific player.";
        public string Help => "mindinfo <session ID>";

        public async void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length != 1)
            {
                shell.WriteLine("Expected exactly 1 argument.");
                return;
            }

            if (!_playerManager.TryGetSessionByUsername(args[0], out var session))
            {
                shell.WriteLine("Can't find that mind");
                return;
            }

            if (await _authorization.TryDenyTargetAsync(
                    shell.Player,
                    session.UserId,
                    Content.Server.Administration.Managers.AdminOperation.GenericTarget,
                    session.Name,
                    shell.WriteError))
            {
                return;
            }

            var minds = _entities.System<SharedMindSystem>();
            if (!minds.TryGetMind(session, out var mindId, out var mind))
            {
                shell.WriteLine("Can't find that mind");
                return;
            }

            var builder = new StringBuilder();
            builder.AppendFormat("player: {0}, mob: {1}\nroles: ", mind.UserId, mind.OwnedEntity);

            var roles = _entities.System<SharedRoleSystem>();
            foreach (var role in roles.MindGetAllRoleInfo(mindId))
            {
                builder.AppendFormat("{0} ", role.Name);
            }

            shell.WriteLine(builder.ToString());
        }
    }
}
