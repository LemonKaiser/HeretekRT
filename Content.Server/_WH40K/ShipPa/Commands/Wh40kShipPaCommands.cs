using System.Linq;
using Content.Server.Administration;
using Content.Shared._WH40K.ShipPa;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.ShipPa.Commands;

public abstract partial class Wh40kShipPaCommand : LocalizedEntityCommands
{
    protected bool TryGetGrid(IConsoleShell shell, string arg, out EntityUid grid)
    {
        grid = default;

        if (!NetEntity.TryParse(arg, out var net)
            || !EntityManager.TryGetEntity(net, out var uid)
            || !EntityManager.HasComponent<MapGridComponent>(uid))
        {
            shell.WriteError(Loc.GetString("cmd-wh40k_shippa-invalid-grid", ("grid", arg)));
            return false;
        }

        grid = uid.Value;
        return true;
    }
}

[AdminCommand(AdminFlags.Fun)]
public sealed partial class Wh40kShipPaAnnounceCommand : Wh40kShipPaCommand
{
    [Dependency] private Wh40kShipPaSystem _pa = default!;

    public override string Command => "wh40k_shippa_announce";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!TryGetGrid(shell, args[0], out var grid))
            return;

        var text = string.Join(' ', args.Skip(1));
        if (!_pa.Announce(grid, text))
            shell.WriteError(Loc.GetString("cmd-wh40k_shippa-no-speakers"));
    }
}

[AdminCommand(AdminFlags.Fun)]
public sealed partial class Wh40kShipPaCodeCommand : Wh40kShipPaCommand
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private Wh40kShipAlertSystem _alert = default!;

    public override string Command => "wh40k_shippa_code";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!TryGetGrid(shell, args[0], out var grid))
            return;

        if (!_proto.HasIndex<Wh40kShipAlertCodePrototype>(args[1]))
        {
            shell.WriteError(Loc.GetString("cmd-wh40k_shippa-invalid-code", ("code", args[1])));
            return;
        }

        _alert.SetCode(grid, args[1]);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 2)
            return CompletionResult.Empty;

        var codes = _proto.EnumeratePrototypes<Wh40kShipAlertCodePrototype>()
            .OrderBy(proto => proto.Order)
            .Select(proto => new CompletionOption(proto.ID));

        return CompletionResult.FromHintOptions(codes, Loc.GetString("cmd-wh40k_shippa-hint-code"));
    }
}

[AdminCommand(AdminFlags.Fun)]
public sealed partial class Wh40kShipPaGeneralQuartersCommand : Wh40kShipPaCommand
{
    [Dependency] private Wh40kShipAlertSystem _alert = default!;

    public override string Command => "wh40k_shippa_gq";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !bool.TryParse(args[1], out var active))
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!TryGetGrid(shell, args[0], out var grid))
            return;

        _alert.SetGeneralQuarters(grid, active);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 2
            ? CompletionResult.FromHintOptions(CompletionHelper.Booleans, Loc.GetString("cmd-wh40k_shippa-hint-active"))
            : CompletionResult.Empty;
    }
}
