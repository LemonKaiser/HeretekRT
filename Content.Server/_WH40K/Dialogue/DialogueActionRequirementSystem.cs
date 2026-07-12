using Content.Server._NF.Bank;
using Content.Shared._WH40K.Dialogue;

namespace Content.Server._WH40K.Dialogue;

/// <summary>
/// Verifies dialogue actions that must succeed before a choice is allowed to change the world.
/// The checks are intentionally side-effect free so a failed choice can play its refusal branch.
/// </summary>
public sealed class DialogueActionRequirementSystem : EntitySystem
{
    [Dependency] private DialogueAccessSystem _access = default!;
    [Dependency] private DialogueItemSystem _items = default!;
    [Dependency] private BankSystem _bank = default!;

    public bool AreRequirementsMet(EntityUid initiator, IReadOnlyList<DialogueActionPrototype> actions)
    {
        foreach (var action in actions)
        {
            // A conditional action may be skipped by a preceding optional action. Its own check must not reject
            // the choice before that preceding action has had a chance to run.
            if (action.OnlyIfPreviousActionSucceeded)
                continue;

            if (!IsRequirementAction(action.Type))
                continue;

            if (!IsActionRequirementMet(initiator, action))
                return false;
        }

        return true;
    }

    public bool IsActionRequirementMet(EntityUid initiator, DialogueActionPrototype action)
    {
        return action.Type switch
        {
            DialogueActionType.TakeItem => action.Prototype != null
                                       && action.Amount > 0
                                       && _items.CanTakeItems(initiator, action.Source, action.Prototype.Value, action.Amount),
            DialogueActionType.DebitBankAccount => action.Amount > 0
                                                    && _bank.CanBankWithdraw(initiator, action.Amount),
            DialogueActionType.CreditBankAccount => action.Amount > 0
                                                     && _bank.CanBankCredit(initiator, action.Amount),
            DialogueActionType.AddAccess => _access.CanModifyAccess(
                initiator,
                action.AccessCardSource,
                action.Accesses,
                add: true),
            DialogueActionType.RemoveAccess => _access.CanModifyAccess(
                initiator,
                action.AccessCardSource,
                action.Accesses,
                add: false),
            _ => true
        };
    }

    public static bool IsRequirementAction(DialogueActionType actionType)
    {
        return actionType is DialogueActionType.TakeItem
            or DialogueActionType.DebitBankAccount
            or DialogueActionType.CreditBankAccount
            or DialogueActionType.AddAccess
            or DialogueActionType.RemoveAccess;
    }
}
