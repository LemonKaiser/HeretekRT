using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server;
using Robust.Shared.Console;

namespace Content.Server._WH40K.PersistentInventory.Commands;

[AdminCommand(AdminFlags.Host)]
public sealed partial class PersistentInventoryShutdownCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IBaseServer _server = default!;

    public string Command => "persistentinv_shutdown";
    public string Description =>
        "Сохраняет подходящие persistent inventory и штатно останавливает сервер. / " +
        "Saves eligible persistent inventories and gracefully shuts down the server.";
    public string Help =>
        "Использование: persistentinv_shutdown [причина]\n" +
        "Usage: persistentinv_shutdown [reason]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TryParseReason(argStr, out var reason, out var error))
        {
            shell.WriteError(error);
            return;
        }

        var result = await _entities.System<PersistentInventoryShutdownSystem>()
            .PrepareAsync($"host-command:{reason}");
        shell.WriteLine(
            $"Shutdown barrier: eligible={result.Eligible}, saved={result.Saved}, " +
            $"failed={result.Failed}, timeout={result.TimedOut}, " +
            $"remainingBound={result.RemainingBound}, epochClean={result.EpochMarkedClean}.");
        _server.Shutdown(reason);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.FromHint("<reason / причина>");
    }

    internal static bool TryParseReason(string argumentText, out string reason, out string error)
    {
        reason = argumentText.Trim();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
            reason = PersistentInventoryCommandHelpers.DefaultReason;
        else if (reason.Length > 512)
        {
            error = "Причина не может превышать 512 символов.";
            return false;
        }

        return true;
    }
}
