using System.Linq;
using Content.Client._WH40K.ShipPreview.UI;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.ShipPreview;

/// <summary>
/// Opens the standalone client-side ship preview window for any vessel prototype.
/// </summary>
public sealed partial class Wh40kShipPreviewCommand : LocalizedCommands
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntitySystemManager _entitySystems = default!;

    public override string Command => "wh40k_shippreview";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number-need-specific", ("properAmount", 1), ("currentAmount", args.Length)));
            return;
        }

        if (!_prototypeManager.TryIndex<VesselPrototype>(args[0], out var vessel))
        {
            shell.WriteError(Loc.GetString("cmd-wh40k_shippreview-unknown", ("id", args[0])));
            return;
        }

        if (!_entitySystems.TryGetEntitySystem<Wh40kShipPreviewSystem>(out _))
        {
            shell.WriteError(Loc.GetString("cmd-wh40k_shippreview-session-required"));
            return;
        }

        Wh40kShipPreviewWindow.Open(vessel);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var vessels = _prototypeManager.EnumeratePrototypes<VesselPrototype>()
            .Where(vessel => !vessel.Abstract)
            .OrderBy(vessel => vessel.ID)
            .Select(vessel => new CompletionOption(vessel.ID, vessel.Name));

        return CompletionResult.FromHintOptions(vessels, Loc.GetString("cmd-wh40k_shippreview-hint-vessel"));
    }
}
