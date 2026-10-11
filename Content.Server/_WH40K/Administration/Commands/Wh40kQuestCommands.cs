using System.Linq;
using Content.Server._WH40K.Quests;
using Content.Server.Administration;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared._WH40K.Quests;
using Robust.Shared.Console;

namespace Content.Server._WH40K.Administration.Commands;

internal static class Wh40kQuestCommandOwner
{
    public static bool TryParse(string value, out Wh40kQuestOwnerKey owner)
    {
        var party = value.StartsWith("party:", StringComparison.Ordinal);
        var valid = Guid.TryParse(party ? value[6..] : value, out var id) && id != Guid.Empty;
        owner = new(party ? Wh40kQuestOwnerScope.Party : Wh40kQuestOwnerScope.Account, id);
        return valid;
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kQuestInspectCommand : IConsoleCommand
{
    [Dependency] private IServerDbManager _db = default!;
    public string Command => "wh40kquest_inspect";
    public string Description => Loc.GetString("wh40k-quest-inspect-description");
    public string Help => Loc.GetString("wh40k-quest-inspect-help");

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !Wh40kQuestCommandOwner.TryParse(args[0], out var owner))
        { shell.WriteError(Help); return; }
        try
        {
            foreach (var quest in (await _db.GetWh40kQuestsAsync(owner)).Concat(await _db.GetWh40kQuestsAsync(owner, history: true)))
                shell.WriteLine($"{quest.Id} {quest.DefinitionId} {(Wh40kQuestState) quest.State} rev={quest.Revision} deadline={quest.Deadline:O}");
            foreach (var intent in await _db.GetWh40kQuestRewardIntentsAsync(owner))
                shell.WriteLine($"intent {intent.RewardId} recipient={intent.Recipient} {(Wh40kQuestRewardState) intent.State}");
            foreach (var submission in await _db.GetWh40kQuestSubmissionsAsync(owner))
                shell.WriteLine($"submission {submission.Id} quest={submission.QuestId} objective={submission.ObjectiveId} units={submission.Amount} {(Wh40kQuestSubmissionState) submission.State} rev={submission.Revision}");
            foreach (var entry in await _db.GetWh40kQuestAuditAsync(owner, 20))
                shell.WriteLine($"audit {entry.At:O} actor={entry.Actor} {entry.Action}: {entry.Reason}");
            if (owner.Scope == Wh40kQuestOwnerScope.Account)
                foreach (var state in await _db.GetWh40kQuestAccountStateAsync(owner.Id)) shell.WriteLine($"state {state.Key} amount={state.Amount} value={state.Value}");
        }
        catch (Exception error) { shell.WriteError(error.Message); }
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kQuestResolveCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    public string Command => "wh40kquest_fail";
    public string Description => Loc.GetString("wh40k-quest-fail-description");
    public string Help => Loc.GetString("wh40k-quest-fail-help");

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 5 || !Wh40kQuestCommandOwner.TryParse(args[0], out var owner) ||
            !Guid.TryParse(args[1], out var quest) || !long.TryParse(args[2], out var revision) || revision < 1 ||
            !Guid.TryParse(args[3], out var operation) || string.IsNullOrWhiteSpace(args[4]))
        { shell.WriteError(Help); return; }
        try
        {
            // The console host has a stable reserved audit identity; players use their authenticated id.
            var actor = shell.Player?.UserId.UserId ?? new Guid("00000000-0000-0000-0000-000000000001");
            var result = await _entities.System<Wh40kQuestSessionSystem>().ResolveForAdminAsync(
                owner, actor,
                new(operation, Wh40kQuestMutation.AdminFail, quest, revision, string.Join(' ', args.Skip(4))));
            shell.WriteLine($"{result.Result}: {result.QuestId} rev={result.Revision}");
        }
        catch (Exception error) { shell.WriteError(error.Message); }
    }
}
