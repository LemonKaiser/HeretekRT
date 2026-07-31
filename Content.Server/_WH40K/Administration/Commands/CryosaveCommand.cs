using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server._WH40K.PersistentInventory;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._WH40K.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class CryosaveCommand : LocalizedCommands
{
    internal const string DefaultReason = "";
    internal const bool AlwaysForce = true;

    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IEntityManager _entities = default!;

    public override string Command => "cryosave";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TryParseArguments(args, out var target, out var reason, out var error))
        {
            shell.WriteError(Loc.GetString(error));
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(target);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("cmd-cryosave-player-not-found"));
            return;
        }

        if (!_players.TryGetSessionById(located.UserId, out var session) ||
            session.AttachedEntity is not { Valid: true } body ||
            !_entities.EntityExists(body))
        {
            shell.WriteError(Loc.GetString("cmd-cryosave-target-not-online"));
            return;
        }

        if (string.IsNullOrEmpty(reason))
            reason = Loc.GetString("cmd-cryosave-default-reason");

        var actor = shell.Player == null
            ? "server-console"
            : $"admin:{shell.Player.UserId}";
        var actorUserId = shell.Player?.UserId.UserId;
        var result = await _entities.System<PersistentInventorySaveSystem>().TrySaveAsync(
            new PersistentInventorySaveRequest(
                located.UserId,
                body,
                session,
                PersistentInventorySaveSource.AdminCommand,
                null,
                actor,
                actorUserId,
                reason,
                Force: AlwaysForce));

        var impact = result.IsSuccess ? LogImpact.High : LogImpact.Medium;
        _adminLog.Add(
            LogType.Mind,
            impact,
            $"Admin cryosave for {located.UserId} by {actor}: result {result.Status}, " +
            $"forced {AlwaysForce}, operation {result.OperationId?.ToString() ?? "<none>"}, reason {reason}.");

        if (result.IsSuccess || result.Status == PersistentInventorySaveStatus.DryRunSuccess)
            shell.WriteLine(result.Message);
        else
            shell.WriteError($"{result.Status}: {result.Message}");
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(players: _players),
                Loc.GetString("cmd-cryosave-hint-target"));
        }

        return args.Length >= 2
            ? CompletionResult.FromHint(Loc.GetString("cmd-cryosave-hint-reason"))
            : CompletionResult.Empty;
    }

    internal static bool TryParseArguments(
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
            error = "cmd-cryosave-help";
            return false;
        }

        target = args[0];
        var reasonIndex = 1;
        while (reasonIndex < args.Count && args[reasonIndex] == "--force")
            reasonIndex++;

        if (reasonIndex < args.Count && args[reasonIndex] == "--reason")
        {
            reasonIndex++;
            if (reasonIndex >= args.Count)
            {
                error = "cmd-cryosave-reason-required";
                return false;
            }
        }

        if (reasonIndex < args.Count)
            reason = string.Join(' ', args.Skip(reasonIndex)).Trim();

        if (reasonIndex < args.Count && string.IsNullOrWhiteSpace(reason))
        {
            error = "cmd-cryosave-reason-empty";
            return false;
        }

        if (reason.Length > 512)
        {
            error = "cmd-cryosave-reason-too-long";
            return false;
        }

        return true;
    }
}
