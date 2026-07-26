using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;
using static Content.Server._WH40K.Administration.Commands.Wh40kRpgGrantCommandHelpers;

namespace Content.Server._WH40K.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kGrantExperienceCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kRpgAdminService _admin = default!;

    public string Command => "wh40kgrantxp";
    public string Description => "Выдаёт аккаунту WH40K RPG опыт с обязательной причиной и аудитом.";
    public string Help => "wh40kgrantxp <игрок или UserId> <XP> <причина>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3 || !long.TryParse(args[1], out var amount) || amount <= 0)
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var target = await ResolveTargetAsync(_locator, args[0]);
            var result = await _admin.GrantExperienceAsync(
                target.UserId,
                target.Username,
                amount,
                CreateAudit(shell, args, 2));
            shell.WriteLine(
                $"Выдано {amount} XP аккаунту {target.Username}; уровень {result.Account.Progress.Level}.");
        }
        catch (Exception exception)
        {
            shell.WriteError(exception.Message);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) =>
        CompletePlayer(_players, args);
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kGrantLevelCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kRpgAdminService _admin = default!;

    public string Command => "wh40kgrantlevel";
    public string Description => "Повышает аккаунт WH40K RPG до указанного уровня через XP-ledger.";
    public string Help => "wh40kgrantlevel <игрок или UserId> <уровень 2-100> <причина>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[1], out var level))
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var target = await ResolveTargetAsync(_locator, args[0]);
            var result = await _admin.GrantTargetLevelAsync(
                target.UserId,
                target.Username,
                level,
                CreateAudit(shell, args, 2));
            shell.WriteLine($"Аккаунт {target.Username} повышен до уровня {result.Account.Progress.Level}.");
        }
        catch (Exception exception)
        {
            shell.WriteError(exception.Message);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) =>
        CompletePlayer(_players, args);
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kGrantDevelopmentPointsCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kRpgAdminService _admin = default!;

    public string Command => "wh40kgrantpoints";
    public string Description => "Выдаёт аккаунту WH40K RPG очки развития с обязательным аудитом.";
    public string Help => "wh40kgrantpoints <игрок или UserId> <количество> <причина>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[1], out var amount) || amount <= 0)
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var target = await ResolveTargetAsync(_locator, args[0]);
            var result = await _admin.GrantDevelopmentPointsAsync(
                target.UserId,
                target.Username,
                amount,
                CreateAudit(shell, args, 2));
            shell.WriteLine(
                $"Аккаунту {target.Username} выдано {amount} очков; доступно " +
                $"{result.Account.Progress.UnspentDevelopmentPoints}.");
        }
        catch (Exception exception)
        {
            shell.WriteError(exception.Message);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) =>
        CompletePlayer(_players, args);
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kCompensateCurrencyCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kRpgAdminService _admin = default!;

    public string Command => "wh40kcompensatecurrency";
    public string Description => "Ставит денежную компенсацию WH40K RPG в постоянную очередь.";
    public string Help => "wh40kcompensatecurrency <игрок или UserId> <сумма> <причина>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3 || !long.TryParse(args[1], out var amount))
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var target = await ResolveTargetAsync(_locator, args[0]);
            await _admin.CompensateCurrencyAsync(
                target.UserId,
                target.Username,
                amount,
                CreateAudit(shell, args, 2));
            shell.WriteLine($"Компенсация {amount} тронных гельтов поставлена для {target.Username}.");
        }
        catch (Exception exception)
        {
            shell.WriteError(exception.Message);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) =>
        CompletePlayer(_players, args);
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kCompensateItemCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kRpgAdminService _admin = default!;

    public string Command => "wh40kcompensateitem";
    public string Description => "Ставит предметную компенсацию WH40K RPG в постоянную очередь.";
    public string Help => "wh40kcompensateitem <игрок или UserId> <прототип> <1-100> <причина>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 4 || !int.TryParse(args[2], out var count))
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var target = await ResolveTargetAsync(_locator, args[0]);
            var prototype = new EntProtoId(args[1]);
            await _admin.CompensateItemAsync(
                target.UserId,
                target.Username,
                prototype,
                count,
                CreateAudit(shell, args, 3));
            shell.WriteLine($"Компенсация {count} x {prototype} поставлена для {target.Username}.");
        }
        catch (Exception exception)
        {
            shell.WriteError(exception.Message);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) =>
        CompletePlayer(_players, args);
}

file static class Wh40kRpgGrantCommandHelpers
{
    public static async Task<LocatedPlayerData> ResolveTargetAsync(IPlayerLocator locator, string value)
    {
        return await locator.LookupIdByNameOrIdAsync(value)
               ?? throw new InvalidOperationException($"Игрок или аккаунт '{value}' не найден.");
    }

    public static Wh40kAdminAudit CreateAudit(IConsoleShell shell, string[] args, int reasonStart)
    {
        var reason = string.Join(' ', args.Skip(reasonStart)).Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Причина обязательна.");

        return shell.Player is { } player
            ? new Wh40kAdminAudit(player.UserId.UserId.ToString("N"), player.Name, reason)
            : new Wh40kAdminAudit("server-console", "server-console", reason);
    }

    public static CompletionResult CompletePlayer(IPlayerManager players, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            players.Sessions.Select(player => player.Name).OrderBy(name => name),
            "<игрок или UserId>");
    }
}
