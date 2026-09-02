using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using static Content.Server._WH40K.Administration.Commands.Wh40kRpgGrantCommandHelpers;

namespace Content.Server._WH40K.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kSetClassCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kRpgAdminService _admin = default!;
    [Dependency] private IAdminAuthorizationManager _authorization = default!;

    public string Command => "wh40ksetclass";
    public string Description => "Меняет постоянный класс аккаунта, очищает его дерево и пишет аудит.";
    public string Help => "wh40ksetclass <игрок или UserId> <classId> <причина>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var target = await ResolveTargetAsync(_locator, args[0]);
            if (!await TryAuthorizeTargetAsync(shell, _authorization, target.UserId, target.Username, AdminOperation.Wh40kClassProgression))
                return;
            var result = await _admin.SetClassAsync(
                target.UserId,
                target.Username,
                args[1],
                CreateAudit(shell, args, 2));
            shell.WriteLine($"Класс аккаунта {target.Username} изменён на {result.Account!.Foundation.ClassId}.");
        }
        catch (Exception exception)
        {
            shell.WriteError(exception.Message);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => CompletePlayer(_players, args);
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kReplaceSkillsCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kRpgAdminService _admin = default!;
    [Dependency] private IAdminAuthorizationManager _authorization = default!;

    public string Command => "wh40kreplaceskills";
    public string Description => "Атомарно заменяет постоянный набор навыков класса и пишет аудит.";
    public string Help => "wh40kreplaceskills <игрок или UserId> <skillId,... или -> <причина>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var skillIds = args[1] == "-"
                ? Array.Empty<string>()
                : args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var target = await ResolveTargetAsync(_locator, args[0]);
            if (!await TryAuthorizeTargetAsync(shell, _authorization, target.UserId, target.Username, AdminOperation.Wh40kClassProgression))
                return;
            var result = await _admin.ReplaceSkillsAsync(
                target.UserId,
                target.Username,
                skillIds,
                CreateAudit(shell, args, 2));
            shell.WriteLine($"У аккаунта {target.Username} теперь {result.ClassProgress!.Skills.Count} навыков класса.");
        }
        catch (Exception exception)
        {
            shell.WriteError(exception.Message);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => CompletePlayer(_players, args);
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kGrantSkillCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kRpgAdminService _admin = default!;
    [Dependency] private IAdminAuthorizationManager _authorization = default!;

    public string Command => "wh40kgrantskill";
    public string Description => "Выдаёт постоянный навык класса с проверкой prerequisite и аудитом.";
    public string Help => "wh40kgrantskill <игрок или UserId> <skillId> <причина>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var target = await ResolveTargetAsync(_locator, args[0]);
            if (!await TryAuthorizeTargetAsync(shell, _authorization, target.UserId, target.Username, AdminOperation.Wh40kClassProgression))
                return;
            var result = await _admin.GrantSkillAsync(
                target.UserId,
                target.Username,
                args[1],
                CreateAudit(shell, args, 2));
            shell.WriteLine($"Навык {args[1]} выдан аккаунту {target.Username}; revision={result.ClassProgress!.Revision}.");
        }
        catch (Exception exception)
        {
            shell.WriteError(exception.Message);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => CompletePlayer(_players, args);
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kRevokeSkillCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kRpgAdminService _admin = default!;
    [Dependency] private IAdminAuthorizationManager _authorization = default!;

    public string Command => "wh40krevokeskill";
    public string Description => "Отзывает навык и зависимые от него навыки с постоянным аудитом.";
    public string Help => "wh40krevokeskill <игрок или UserId> <skillId> <причина>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var target = await ResolveTargetAsync(_locator, args[0]);
            if (!await TryAuthorizeTargetAsync(shell, _authorization, target.UserId, target.Username, AdminOperation.Wh40kClassProgression))
                return;
            var result = await _admin.RevokeSkillAsync(
                target.UserId,
                target.Username,
                args[1],
                CreateAudit(shell, args, 2));
            shell.WriteLine($"Навык {args[1]} отозван у {target.Username}; revision={result.ClassProgress!.Revision}.");
        }
        catch (Exception exception)
        {
            shell.WriteError(exception.Message);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => CompletePlayer(_players, args);
}
