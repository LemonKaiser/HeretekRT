using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Database;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.PersistentInventory.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class PersistentInventoryStatusCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IServerDbManager _db = default!;

    public string Command => "persistentinv_status";
    public string Description =>
        "Показывает серверное состояние persistent inventory аккаунта. / " +
        "Shows the account's server-side persistent inventory state.";
    public string Help =>
        "Использование: persistentinv_status <игрок|UserId>\n" +
        "Usage: persistentinv_status <player|UserId>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!PersistentInventoryCommandHelpers.TryGetTarget(shell, args, out var target))
            return;

        try
        {
            var player = await _locator.LookupIdByNameOrIdAsync(target);
            if (player == null)
            {
                shell.WriteError("Игрок или UserId не найден.");
                return;
            }

            var header = await _db.GetPersistentInventoryHeaderAsync(player.UserId);
            if (header == null)
            {
                shell.WriteLine($"{player.Username} ({player.UserId}): снимка нет.");
                return;
            }

            var latestLostSnapshot = await _db.GetLatestPersistentInventoryLostSnapshotAsync(player.UserId);
            shell.WriteLine($"{player.Username} ({player.UserId}):");
            shell.WriteLine(
                $"  State={header.State}, VerifiedState={header.VerifiedState}, " +
                $"SavePhase={header.SavePhase}, Revision={header.Revision}, OperationId={header.OperationId}");
            shell.WriteLine($"  Current={PersistentInventoryCommandHelpers.Format(header.CurrentVerified?.SnapshotId)}");
            shell.WriteLine($"  LastKnownGood={PersistentInventoryCommandHelpers.Format(header.LastKnownGood?.SnapshotId)}");
            shell.WriteLine($"  LatestLostByDisconnect={PersistentInventoryCommandHelpers.Format(latestLostSnapshot)}");
            shell.WriteLine($"  Staging={PersistentInventoryCommandHelpers.Format(header.Staging?.SnapshotId)}");
            shell.WriteLine($"  ServerEpoch={PersistentInventoryCommandHelpers.Format(header.ServerEpoch)}");
            shell.WriteLine($"  StagingServerEpoch={PersistentInventoryCommandHelpers.Format(header.StagingServerEpoch)}");
            shell.WriteLine($"  WorldCleanupAuthorizedAt={header.WorldCleanupAuthorizedAt?.ToString("O") ?? "<нет>"}");
            shell.WriteLine($"  LifeId={PersistentInventoryCommandHelpers.Format(header.LifeId)}");
            shell.WriteLine($"  UpdatedAt={header.UpdatedAt:O}");
        }
        catch (Exception exception)
        {
            shell.WriteError($"Не удалось прочитать persistent inventory: {exception.GetType().Name}.");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return PersistentInventoryCommandHelpers.CompleteTarget(args);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class PersistentInventoryHistoryCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IServerDbManager _db = default!;

    public string Command => "persistentinv_history";
    public string Description =>
        "Показывает неизменяемую историю persistent inventory аккаунта. / " +
        "Shows the account's append-only persistent inventory audit.";
    public string Help =>
        "Использование: persistentinv_history <игрок|UserId> [1..100]\n" +
        "Usage: persistentinv_history <player|UserId> [1..100]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Help);
            return;
        }

        var limit = 50;
        if (args.Length == 2 && (!int.TryParse(args[1], out limit) || limit is < 1 or > 100))
        {
            shell.WriteError("Лимит истории должен быть целым числом от 1 до 100.");
            return;
        }

        try
        {
            var player = await _locator.LookupIdByNameOrIdAsync(args[0]);
            if (player == null)
            {
                shell.WriteError("Игрок или UserId не найден.");
                return;
            }

            var history = await _db.GetPersistentInventoryAuditAsync(player.UserId, limit);
            if (history.Count == 0)
            {
                shell.WriteLine($"{player.Username} ({player.UserId}): история пуста.");
                return;
            }

            shell.WriteLine($"Persistent inventory audit для {player.Username} ({player.UserId}):");
            foreach (var entry in history)
            {
                shell.WriteLine(
                    $"  [{entry.Id}] {entry.CreatedAt:O} {entry.Action} " +
                    $"{entry.OldState}->{entry.NewState} rev={entry.Revision} " +
                    $"snapshot={PersistentInventoryCommandHelpers.Format(entry.SnapshotId)} " +
                    $"op={entry.OperationId} actor={entry.Actor} reason={entry.Reason ?? "<нет>"}");
            }
        }
        catch (Exception exception)
        {
            shell.WriteError($"Не удалось прочитать audit persistent inventory: {exception.GetType().Name}.");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return PersistentInventoryCommandHelpers.CompleteTarget(args);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class PersistentInventoryQuarantineReasonCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IServerDbManager _db = default!;

    public string Command => "persistentinv_quarantine_reason";
    public string Description =>
        "Показывает причину карантина persistent inventory аккаунта. / " +
        "Shows why the account's persistent inventory was quarantined.";
    public string Help =>
        "Использование: persistentinv_quarantine_reason <игрок|UserId>\n" +
        "Usage: persistentinv_quarantine_reason <player|UserId>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!PersistentInventoryCommandHelpers.TryGetTarget(shell, args, out var target))
            return;

        try
        {
            var player = await _locator.LookupIdByNameOrIdAsync(target);
            if (player == null)
            {
                shell.WriteError("Игрок или UserId не найден.");
                return;
            }

            var header = await _db.GetPersistentInventoryHeaderAsync(player.UserId);
            if (header == null)
            {
                shell.WriteLine($"{player.Username} ({player.UserId}): снимка нет.");
                return;
            }

            if (header.State != PersistentInventorySnapshotState.Quarantined)
            {
                shell.WriteLine(
                    $"{player.Username} ({player.UserId}): state={header.State}, снимок не находится в карантине.");
                return;
            }

            shell.WriteLine(
                $"{player.Username} ({player.UserId}): quarantine={header.QuarantineReason}, " +
                $"details={header.ReasonDetails ?? "<нет>"}.");
        }
        catch (Exception exception)
        {
            shell.WriteError($"Не удалось прочитать причину карантина: {exception.GetType().Name}.");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return PersistentInventoryCommandHelpers.CompleteTarget(args);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class PersistentInventoryInvalidateCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "persistentinv_invalidate";
    public string Description =>
        "Инвалидирует persistent inventory без удаления предметов мира. / " +
        "Invalidates persistent inventory without deleting world items.";
    public string Help =>
        "Использование: persistentinv_invalidate <игрок|UserId> [причина]\n" +
        "Usage: persistentinv_invalidate <player|UserId> [reason]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        await PersistentInventoryCommandHelpers.ExecuteMutation(
            shell,
            args,
            _locator,
            _systems,
            (system, userId, actor, actorUserId, reason) =>
                system.AdminInvalidateAsync(userId, actor, actorUserId, reason));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return PersistentInventoryCommandHelpers.CompleteMutation(args);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class PersistentInventoryQuarantineCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "persistentinv_quarantine";
    public string Description =>
        "Помещает persistent inventory в карантин без выдачи или удаления предметов. / " +
        "Quarantines persistent inventory without granting or deleting items.";
    public string Help =>
        "Использование: persistentinv_quarantine <игрок|UserId> [причина]\n" +
        "Usage: persistentinv_quarantine <player|UserId> [reason]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        await PersistentInventoryCommandHelpers.ExecuteMutation(
            shell,
            args,
            _locator,
            _systems,
            (system, userId, actor, actorUserId, reason) =>
                system.AdminQuarantineAsync(userId, actor, actorUserId, reason));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return PersistentInventoryCommandHelpers.CompleteMutation(args);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class PersistentInventoryRollbackCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "persistentinv_rollback";
    public string Description =>
        "Переключает persistent inventory на указанный подтверждённый snapshot. / " +
        "Switches persistent inventory to the specified verified snapshot.";
    public string Help =>
        "Использование: persistentinv_rollback <игрок|UserId> <snapshotId> [причина]\n" +
        "Usage: persistentinv_rollback <player|UserId> <snapshotId> [reason]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        await PersistentInventoryCommandHelpers.ExecuteRollback(shell, args, _locator, _systems);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return PersistentInventoryCommandHelpers.CompleteRollback(args);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class PersistentInventoryRecoverLostCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "persistentinv_recover_lost";
    public string Description =>
        "Восстанавливает последний доступный LostByDisconnect snapshot. / " +
        "Restores the latest available LostByDisconnect snapshot.";
    public string Help =>
        "Использование: persistentinv_recover_lost <игрок|UserId> [причина]\n" +
        "Usage: persistentinv_recover_lost <player|UserId> [reason]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        await PersistentInventoryCommandHelpers.ExecuteMutation(
            shell,
            args,
            _locator,
            _systems,
            (system, userId, actor, actorUserId, reason) =>
                system.AdminRecoverLostAsync(userId, actor, actorUserId, reason));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return PersistentInventoryCommandHelpers.CompleteMutation(args);
    }
}

internal static class PersistentInventoryCommandHelpers
{
    internal const string DefaultReason = "Причина не указана.";

    public static bool TryGetTarget(IConsoleShell shell, string[] args, out string target)
    {
        target = string.Empty;
        if (args.Length != 1)
        {
            shell.WriteError("Ожидался ровно один аргумент: имя игрока или UserId.");
            return false;
        }

        target = args[0];
        return true;
    }

    public static CompletionResult CompleteTarget(string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(),
                "<player|UserId / игрок|UserId>")
            : CompletionResult.Empty;
    }

    public static CompletionResult CompleteMutation(string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(),
                "<player|UserId / игрок|UserId>");
        }

        return CompletionResult.FromHint("<reason / причина>");
    }

    public static CompletionResult CompleteRollback(string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(),
                "<player|UserId / игрок|UserId>");
        }

        return args.Length == 2
            ? CompletionResult.FromHint("<snapshotId>")
            : CompletionResult.FromHint("<reason / причина>");
    }

    public static async Task ExecuteRollback(
        IConsoleShell shell,
        string[] args,
        IPlayerLocator locator,
        IEntitySystemManager systems)
    {
        if (!TryParseRollbackArguments(
                args,
                out var target,
                out var snapshotId,
                out var reason,
                out var error))
        {
            shell.WriteError(error);
            return;
        }

        try
        {
            var player = await locator.LookupIdByNameOrIdAsync(target);
            if (player == null)
            {
                shell.WriteError("Игрок или UserId не найден.");
                return;
            }

            var actor = shell.Player?.Name ?? "server-console";
            var actorUserId = shell.Player?.UserId.UserId;
            var result = await systems
                .GetEntitySystem<PersistentInventoryLifecycleSystem>()
                .AdminRollbackAsync(
                    player.UserId,
                    snapshotId,
                    actor,
                    actorUserId,
                    reason);
            if (result.Success)
                shell.WriteLine(result.Message);
            else
                shell.WriteError(result.Message);
        }
        catch (Exception exception)
        {
            shell.WriteError($"Rollback persistent inventory не выполнен: {exception.GetType().Name}.");
        }
    }

    public static async Task ExecuteMutation(
        IConsoleShell shell,
        string[] args,
        IPlayerLocator locator,
        IEntitySystemManager systems,
        Func<
            PersistentInventoryLifecycleSystem,
            Robust.Shared.Network.NetUserId,
            string,
            Guid?,
            string,
            Task<PersistentInventoryAdminMutationResult>> mutation)
    {
        if (!TryParseMutationArguments(args, out var target, out var reason, out var error))
        {
            shell.WriteError(error);
            return;
        }

        try
        {
            var player = await locator.LookupIdByNameOrIdAsync(target);
            if (player == null)
            {
                shell.WriteError("Игрок или UserId не найден.");
                return;
            }

            var actor = shell.Player?.Name ?? "server-console";
            var actorUserId = shell.Player?.UserId.UserId;
            var result = await mutation(
                systems.GetEntitySystem<PersistentInventoryLifecycleSystem>(),
                player.UserId,
                actor,
                actorUserId,
                reason);
            if (result.Success)
                shell.WriteLine(result.Message);
            else
                shell.WriteError(result.Message);
        }
        catch (Exception exception)
        {
            shell.WriteError($"Операция persistent inventory не выполнена: {exception.GetType().Name}.");
        }
    }

    internal static bool TryParseMutationArguments(
        IReadOnlyList<string> args,
        out string target,
        out string reason,
        out string error)
    {
        target = string.Empty;
        reason = DefaultReason;
        error = string.Empty;
        if (args.Count < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            error = "Ожидался игрок или UserId.";
            return false;
        }

        target = args[0];
        if (args.Count == 1)
            return true;

        reason = string.Join(' ', args.Skip(1)).Trim();
        if (reason.Length == 0)
        {
            reason = DefaultReason;
            return true;
        }

        if (reason.Length > 512)
        {
            error = "Причина не может превышать 512 символов.";
            return false;
        }

        return true;
    }

    internal static bool TryParseRollbackArguments(
        IReadOnlyList<string> args,
        out string target,
        out PersistentInventorySnapshotId snapshotId,
        out string reason,
        out string error)
    {
        target = string.Empty;
        snapshotId = default;
        reason = DefaultReason;
        error = string.Empty;
        if (args.Count < 2 || string.IsNullOrWhiteSpace(args[0]))
        {
            error = "Ожидались игрок или UserId и snapshotId.";
            return false;
        }

        target = args[0];
        if (!Guid.TryParse(args[1], out var parsedSnapshotId) || parsedSnapshotId == Guid.Empty)
        {
            error = "snapshotId должен быть непустым UUID.";
            return false;
        }

        snapshotId = new PersistentInventorySnapshotId(parsedSnapshotId);
        if (args.Count == 2)
            return true;

        reason = string.Join(' ', args.Skip(2)).Trim();
        if (reason.Length == 0)
        {
            reason = DefaultReason;
            return true;
        }

        if (reason.Length > 512)
        {
            error = "Причина не может превышать 512 символов.";
            return false;
        }

        return true;
    }

    public static string Format<T>(T? value) where T : struct
    {
        return value?.ToString() ?? "<нет>";
    }
}
