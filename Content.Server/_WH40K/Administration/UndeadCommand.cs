using Content.Server.Administration;
using Content.Shared._WH40K.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._WH40K.Administration;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class UndeadCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;

    public string Command => "undead";
    public string Description => Loc.GetString("wh40k-undead-description");
    public string Help => Loc.GetString("wh40k-undead-help");

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players),
                Loc.GetString("wh40k-undead-target-hint"))
            : CompletionResult.Empty;
    }

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        EntityUid target;
        switch (args.Length)
        {
            case 0:
                if (shell.Player?.AttachedEntity is not { } attached)
                {
                    shell.WriteError(Loc.GetString("wh40k-undead-no-target"));
                    return;
                }

                target = attached;
                break;
            case 1:
                if (_players.TryGetSessionByUsername(args[0], out var player))
                {
                    if (player.AttachedEntity is not { } controlled || !_entities.EntityExists(controlled))
                    {
                        shell.WriteError(Loc.GetString("wh40k-undead-player-no-entity", ("player", player.Name)));
                        return;
                    }

                    target = controlled;
                }
                else if (NetEntity.TryParse(args[0], out var net)
                    && _entities.TryGetEntity(net, out var entity)
                    && _entities.EntityExists(entity))
                {
                    target = entity.Value;
                }
                else
                {
                    shell.WriteError(Loc.GetString("wh40k-undead-invalid-target", ("target", args[0])));
                    return;
                }

                break;
            default:
                shell.WriteError(Help);
                return;
        }

        var system = _entities.System<UndeadSystem>();
        if (!_entities.HasComponent<UndeadComponent>(target) && !system.CanEnable(target))
        {
            shell.WriteError(Loc.GetString("wh40k-undead-invalid-mob"));
            return;
        }

        var enabled = system.ToggleUndead(target);
        shell.WriteLine(Loc.GetString(enabled ? "wh40k-undead-enabled" : "wh40k-undead-disabled",
            ("entity", _entities.GetComponent<MetaDataComponent>(target).EntityName)));
    }
}
