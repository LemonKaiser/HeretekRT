using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._WH40K.Activities;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Activities.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class KoronusActivityStatusCommand : IConsoleCommand
{
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "koronusactivity_status";
    public string Description => "Показывает read-only состояние director-а активностей Koronus.";
    public string Help => "Использование: koronusactivity_status";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var director = _systems.GetEntitySystem<KoronusActivityDirectorSystem>();
        if (!director.TryGetStatus(out var status))
        {
            shell.WriteError("Director активностей не запущен: нужен активный раунд Rogue Trader.");
            return;
        }

        shell.WriteLine(
            $"Koronus activity director: seed={status.RoundSeed}; day={status.DayIndex}; focus={status.DayFocus}; " +
            $"active={status.ActiveCount}; journal={status.HistoryCount}; nextSpawn={status.NextSpawnAt:hh\\:mm\\:ss}.");
        foreach (var instance in director.GetActiveInstances())
        {
            var route = instance.RouteFromSystem == null
                ? instance.SystemId
                : $"{instance.RouteFromSystem}->{instance.RouteToSystem}";
            shell.WriteLine(
                $"  #{instance.Id} {instance.TemplateId}; target={route}; state={instance.State}; " +
                $"expires={instance.ExpiresAt:hh\\:mm\\:ss}.");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => CompletionResult.Empty;
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class KoronusActivityResolveCommand : IConsoleCommand
{
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "koronusactivity_resolve";
    public string Description => "Безопасно завершает data-only активность Koronus для проверки lifecycle.";
    public string Help => "Использование: koronusactivity_resolve <instanceId> <complete|abandon>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !long.TryParse(args[0], out var instanceId) || instanceId <= 0)
        {
            shell.WriteError(Help);
            return;
        }

        var reason = args[1].ToLowerInvariant() switch
        {
            "complete" => KoronusActivityTerminalReason.Completed,
            "abandon" => KoronusActivityTerminalReason.Abandoned,
            _ => KoronusActivityTerminalReason.RoundEnded,
        };
        if (reason == KoronusActivityTerminalReason.RoundEnded)
        {
            shell.WriteError(Help);
            return;
        }

        var director = _systems.GetEntitySystem<KoronusActivityDirectorSystem>();
        if (!director.TryResolve(instanceId, reason))
        {
            shell.WriteError("Активность не найдена, уже завершена или director не запущен.");
            return;
        }

        shell.WriteLine($"Активность #{instanceId} завершена с исходом {reason}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => CompletionResult.Empty;
}
