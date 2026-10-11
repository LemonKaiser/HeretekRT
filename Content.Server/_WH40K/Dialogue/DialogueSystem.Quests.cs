using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Quests;
using Content.Shared._WH40K.Dialogue;
using Content.Shared._WH40K.Quests;

namespace Content.Server._WH40K.Dialogue;

public sealed partial class DialogueSystem
{
    private static bool IsQuestAction(DialogueActionPrototype action) => action.Type >= DialogueActionType.OfferQuest;

    private static bool HasQuestSteps(IReadOnlyList<DialogueStep> steps) => steps.Any(step => step.Actions.Any(IsQuestAction) ||
        step.Choices.Any(choice => choice.Actions.Any(IsQuestAction) || HasQuestSteps(choice.ResponseSteps) || HasQuestSteps(choice.FailureResponseSteps)));

    private static bool ValidQuestRevision(ActiveDialogueSession session, int revision) =>
        revision == session.LineRevision || revision == -1 && !HasQuestSteps(session.Prototype.Steps);

    private void BeginQuestActions(ActiveDialogueSession session, IReadOnlyList<DialogueActionPrototype> actions, Action<bool> continuation)
    {
        session.QuestPending = true;
        session.QuestPendingSince = _timing.CurTime;
        RaiseNetworkEvent(new DialogueOperationEvent(session.SessionId, true, Wh40kQuestOperationResult.Pending), session.Session);
        EntityManager.System<Wh40kQuestSessionSystem>().TrackWork(RunQuestActionsAsync(session, actions, continuation));
    }

    private async Task RunQuestActionsAsync(ActiveDialogueSession session, IReadOnlyList<DialogueActionPrototype> actions, Action<bool> continuation)
    {
        var quests = EntityManager.System<Wh40kQuestSessionSystem>();
        var generation = quests.Generation;
        var id = session.SessionId;
        var result = Wh40kQuestOperationResult.Succeeded;
        var previous = true;
        bool Current() => generation == quests.Generation && !quests.IsStopping && session.SessionId == id &&
            _sessions.TryGetValue(session.Session, out var current) && ReferenceEquals(current, session) &&
            session.Session.AttachedEntity == session.Initiator && CanContinueDialogue(session);
        try
        {
            foreach (var action in actions)
            {
                var alive = false;
                Task<Wh40kQuestResult>? work = null;
                await quests.OnSimulationThread(() =>
                {
                    alive = Current();
                    if (!alive || action.OnlyIfPreviousActionSucceeded && !previous) return;
                    if (IsQuestAction(action))
                    {
                        // Reuse OpenMerchant's handoff: the input lease blocks opening BUIs.
                        if (action.Type == DialogueActionType.OpenQuestLog)
                        {
                            session.Closing = true;
                            session.AbortRequested = true;
                            CloseSession(session, sendCloseEvent: true);
                        }
                        work = EntityManager.System<Wh40kQuestInteractionSystem>().ExecuteDialogueAsync(session.UserId,
                            session.Initiator, session.Target, action, Guid.NewGuid(), Current);
                    }
                    else previous = ExecuteActionCore(session, session.UserId, session.Initiator, session.Target, action);
                });
                if (!alive) return;
                if (work != null)
                {
                    result = (await work).Result;
                    previous = result == Wh40kQuestOperationResult.Succeeded;
                    if (action.Type == DialogueActionType.OpenQuestLog) return;
                    if (!previous) break;
                }
                else if (!previous && DialogueActionRequirementSystem.IsRequirementAction(action.Type)) break;
            }
        }
        catch (Exception error)
        {
            Log.Warning($"Quest dialogue operation failed; durable results remain recoverable: {error.Message}");
            result = Wh40kQuestOperationResult.PersistenceUnavailable;
            previous = false;
        }
        await quests.OnSimulationThread(() =>
        {
            if (!Current()) return;
            session.QuestPending = false;
            if (previous && actions.Any(x => x.Type == DialogueActionType.OpenQuestLog)) session.AbortRequested = true;
            continuation(previous);
            RaiseNetworkEvent(new DialogueOperationEvent(id, false, result), session.Session);
        });
    }
}
