using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Materials;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Economy;

[AdminCommand(AdminFlags.Debug)]
public sealed class MaterialMarketCommand : IConsoleCommand
{
    public string Command => "materialmarket";
    public string Description => Loc.GetString("wh40k-market-command-description");
    public string Help => Loc.GetString("wh40k-market-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var entities = IoCManager.Resolve<IEntityManager>();
        var market = entities.System<MaterialMarketSystem>();
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        if (args.Length == 2 && args[0] == "reset" && prototypes.HasIndex<MaterialPrototype>(args[1]))
        {
            shell.WriteLine(market.ResetDemand(args[1])
                ? Loc.GetString("wh40k-market-command-reset") : Loc.GetString("wh40k-market-unavailable"));
            return;
        }
        if (args.Length > 1 || args.Length == 1 && !prototypes.HasIndex<MaterialPrototype>(args[0]))
        {
            shell.WriteError(Help);
            return;
        }
        foreach (var material in prototypes.EnumeratePrototypes<MaterialPrototype>().OrderBy(proto => proto.ID))
        {
            if (args.Length == 1 && material.ID != args[0])
                continue;
            var load = market.GetLoad(material.ID);
            var demand = market.GetDemandFactor(material.ID);
            if (load == 0 && demand == 1 && args.Length == 0)
                continue;
            shell.WriteLine($"{material.ID}: price={market.GetUnitPrice(material.ID):0.######}, load={load:0.##}, " +
                            $"recovery={market.Profile.RecoveryPerSecond:0.##}/s, demand={demand:0.###}, " +
                            $"cycle={market.DemandSecondsRemaining}s");
        }
    }
}
