using Content.Server.Administration;
using Content.Server._WH40K.Bosses;
using Content.Shared.Administration;
using Content.Shared._WH40K.Bosses;
using Robust.Shared.Console;

namespace Content.Server._WH40K.Administration;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class ChaosMarineAttackCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    public string Command => "wh40k_marine_attack";
    public string Description => Loc.GetString("wh40k-marine-attack-command");
    public string Help => Loc.GetString("wh40k-marine-attack-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3 || !NetEntity.TryParse(args[0], out var bossNet) ||
            !NetEntity.TryParse(args[2], out var targetNet) ||
            !_entities.TryGetEntity(bossNet, out var boss) || boss == null ||
            !_entities.TryGetEntity(targetNet, out var target) || target == null ||
            !Enum.TryParse<MarineAttack>(args[1], true, out var attack))
        {
            shell.WriteError(Help);
            return;
        }
        var additional = new List<EntityUid>();
        foreach (var argument in args[3..])
        {
            if (!NetEntity.TryParse(argument, out var net) || !_entities.TryGetEntity(net, out var entity) || entity == null)
            {
                shell.WriteError(Help);
                return;
            }
            additional.Add(entity.Value);
        }
        if (!_entities.System<Wh40kChaosMarineSystem>().TryStartAttack(boss.Value, attack, target.Value, additional.ToArray()))
            shell.WriteError(Help);
    }
}
