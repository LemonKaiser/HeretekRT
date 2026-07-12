using System;
using System.Collections.Generic;
using Content.Shared.Access;
using Content.Shared._WH40K.Dialogue;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Dialogue;

/// <summary>
/// Performs semantic validation that cannot be expressed by data-field requirements alone.
/// Diagnostics contain stable codes and YAML-like paths so content errors are actionable and testable.
/// </summary>
public static class DialoguePrototypeValidator
{
    public static DialoguePrototypeValidationResult Validate(
        DialoguePrototype prototype,
        IPrototypeManager prototypeManager)
    {
        var diagnostics = new List<DialoguePrototypeDiagnostic>();
        var rootStepIndices = BuildRootStepIndices(prototype, diagnostics);
        var participants = BuildParticipants(prototype, prototypeManager, diagnostics);

        ValidatePositiveFinite(prototype.TypewriterCps, "typewriterCps", "invalid-typewriter-cps", diagnostics);
        ValidateScene(prototype.Scene, diagnostics);
        ValidateActions(prototype.StartActions, "startActions", prototypeManager, participants, diagnostics);
        ValidateActions(prototype.CompleteActions, "completeActions", prototypeManager, participants, diagnostics);
        ValidateRequirementActionPlacement(prototype.StartActions, "startActions", diagnostics);
        ValidateRequirementActionPlacement(prototype.CompleteActions, "completeActions", diagnostics);

        if (prototype.InteractionMode == DialogueInteractionMode.Personal)
        {
            ValidatePersonalActions(prototype.StartActions, "startActions", diagnostics);
            ValidatePersonalActions(prototype.CompleteActions, "completeActions", diagnostics);
            ValidatePersonalSteps(prototype.Steps, "steps", prototypeManager, diagnostics);
        }

        if (prototype.Steps.Count == 0)
        {
            Add(diagnostics, "empty-dialogue", "steps", "Dialogue has no configured steps.");
        }
        else
        {
            ValidateSteps(prototype.Steps, "steps", rootStepIndices, prototypeManager, participants, diagnostics);
        }

        return new DialoguePrototypeValidationResult(rootStepIndices, diagnostics);
    }

    private static IReadOnlyDictionary<string, DialogueParticipantPrototype> BuildParticipants(
        DialoguePrototype prototype,
        IPrototypeManager prototypeManager,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        var participants = new Dictionary<string, DialogueParticipantPrototype>(StringComparer.Ordinal);

        for (var i = 0; i < prototype.Participants.Count; i++)
        {
            var participant = prototype.Participants[i];
            var path = $"participants[{i}]";

            if (string.IsNullOrWhiteSpace(participant.Id))
            {
                Add(diagnostics, "missing-participant-id", $"{path}.id", "A dialogue participant requires an id.");
                continue;
            }

            if (participant.Id is "initiator" or "npc")
            {
                Add(diagnostics, "reserved-participant-id", $"{path}.id", $"Participant id '{participant.Id}' is reserved.");
                continue;
            }

            if (!participants.TryAdd(participant.Id, participant))
            {
                Add(diagnostics, "duplicate-participant-id", $"{path}.id", $"Participant id '{participant.Id}' is already used.");
                continue;
            }

            if (participant.Portrait != null)
                ValidatePortraitPrototype(participant.Portrait, $"{path}.portrait", prototypeManager, diagnostics);

            foreach (var (expression, portrait) in participant.Expressions)
            {
                if (string.IsNullOrWhiteSpace(expression))
                {
                    Add(diagnostics, "missing-expression-id", $"{path}.expressions", "Expression ids cannot be empty.");
                    continue;
                }

                ValidatePortraitPrototype(
                    portrait,
                    $"{path}.expressions.{expression}",
                    prototypeManager,
                    diagnostics);
            }
        }

        foreach (var (id, participant) in participants)
        {
            ValidateLocalizationArguments(
                participant.NameArgs,
                $"participants[{id}].nameArgs",
                prototypeManager,
                participants,
                diagnostics);
        }

        return participants;
    }

    private static Dictionary<string, int> BuildRootStepIndices(
        DialoguePrototype prototype,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < prototype.Steps.Count; i++)
        {
            var id = prototype.Steps[i].Id;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (!indices.TryAdd(id, i))
            {
                Add(
                    diagnostics,
                    "duplicate-step-id",
                    $"steps[{i}].id",
                    $"Root step id '{id}' is already used.");
            }
        }

        return indices;
    }

    private static void ValidateScene(
        DialogueScenePrototype scene,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        ValidateRange(scene.DimOpacity, 0f, 1f, "scene.dimOpacity", "invalid-dim-opacity", diagnostics);
        ValidateRange(
            scene.InactiveActorOpacity,
            0f,
            1f,
            "scene.inactiveActorOpacity",
            "invalid-inactive-actor-opacity",
            diagnostics);
        ValidatePositiveFinite(scene.WindowWidth, "scene.windowWidth", "invalid-window-width", diagnostics);
        ValidatePositiveFinite(scene.WindowMinHeight, "scene.windowMinHeight", "invalid-window-min-height", diagnostics);
        ValidatePositiveFinite(scene.WindowMaxHeight, "scene.windowMaxHeight", "invalid-window-max-height", diagnostics);

        if (IsFinite(scene.WindowMinHeight)
            && IsFinite(scene.WindowMaxHeight)
            && scene.WindowMinHeight > scene.WindowMaxHeight)
        {
            Add(
                diagnostics,
                "invalid-window-height-order",
                "scene.windowMinHeight",
                "windowMinHeight cannot be greater than windowMaxHeight.");
        }

        ValidateNonNegativeFinite(scene.WindowMargin, "scene.windowMargin", "invalid-window-margin", diagnostics);
        ValidatePositiveFinite(scene.ActorScale, "scene.actorScale", "invalid-actor-scale", diagnostics);
        ValidatePositiveFinite(scene.ActorWidth, "scene.actorWidth", "invalid-actor-width", diagnostics);
        ValidatePositiveFinite(scene.ActorHeight, "scene.actorHeight", "invalid-actor-height", diagnostics);
        ValidateNonNegativeFinite(scene.ActorGap, "scene.actorGap", "invalid-actor-gap", diagnostics);
        ValidateNonNegativeFinite(scene.ActorOverlap, "scene.actorOverlap", "invalid-actor-overlap", diagnostics);
        ValidateNonNegativeFinite(
            scene.ActorWindowOverlap,
            "scene.actorWindowOverlap",
            "invalid-actor-window-overlap",
            diagnostics);
        ValidateFinite(scene.ActorStageOffsetY, "scene.actorStageOffsetY", "invalid-actor-stage-offset", diagnostics);
        ValidateRange(scene.LeftActorAlignmentX, 0f, 1f, "scene.leftActorAlignmentX", "invalid-left-actor-alignment", diagnostics);
        ValidateRange(scene.RightActorAlignmentX, 0f, 1f, "scene.rightActorAlignmentX", "invalid-right-actor-alignment", diagnostics);
        ValidateFinite(scene.LeftActorOffsetX, "scene.leftActorOffsetX", "invalid-left-actor-offset-x", diagnostics);
        ValidateFinite(scene.LeftActorOffsetY, "scene.leftActorOffsetY", "invalid-left-actor-offset-y", diagnostics);
        ValidateFinite(scene.RightActorOffsetX, "scene.rightActorOffsetX", "invalid-right-actor-offset-x", diagnostics);
        ValidateFinite(scene.RightActorOffsetY, "scene.rightActorOffsetY", "invalid-right-actor-offset-y", diagnostics);

        if (scene.SpeakerFontSize <= 0)
            Add(diagnostics, "invalid-speaker-font-size", "scene.speakerFontSize", "Font size must be greater than zero.");
        if (scene.BodyFontSize <= 0)
            Add(diagnostics, "invalid-body-font-size", "scene.bodyFontSize", "Font size must be greater than zero.");
        if (scene.ContinueFontSize <= 0)
            Add(diagnostics, "invalid-continue-font-size", "scene.continueFontSize", "Font size must be greater than zero.");

        ValidateNonNegativeFinite(
            scene.BackgroundMusicDuckGain,
            "scene.backgroundMusicDuckGain",
            "invalid-background-music-duck-gain",
            diagnostics);
        ValidateMusic(scene.Music, "scene.music", diagnostics);
    }

    private static void ValidateSteps(
        IReadOnlyList<DialogueStep> steps,
        string path,
        IReadOnlyDictionary<string, int> rootStepIndices,
        IPrototypeManager prototypeManager,
        IReadOnlyDictionary<string, DialogueParticipantPrototype> participants,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var stepPath = $"{path}[{i}]";

            if (string.IsNullOrWhiteSpace(step.Text.Id))
                Add(diagnostics, "missing-step-text", $"{stepPath}.text", "Step text is required.");

            ValidateLocalizationArguments(step.TextArgs, $"{stepPath}.textArgs", prototypeManager, participants, diagnostics);
            ValidateStepPresentation(step, stepPath, participants, diagnostics);
            ValidateActions(step.Actions, $"{stepPath}.actions", prototypeManager, participants, diagnostics);
            ValidateRequirementActionPlacement(step.Actions, $"{stepPath}.actions", diagnostics);
            ValidateMusic(step.Music, $"{stepPath}.music", diagnostics);

            if (step.AutoAdvanceAfter is { } autoAdvance)
                ValidateNonNegativeFinite(autoAdvance, $"{stepPath}.autoAdvanceAfter", "invalid-auto-advance", diagnostics);

            ValidateRootStepReference(step.NextStep, $"{stepPath}.nextStep", rootStepIndices, diagnostics);

            if (step.Type != DialogueStepType.Choice)
            {
                if (step.Choices.Count > 0)
                {
                    Add(
                        diagnostics,
                        "choices-on-line-step",
                        $"{stepPath}.choices",
                        "A non-choice step cannot contain choices. Did you forget 'type: choice'?");
                }

                continue;
            }

            if (step.Choices.Count == 0)
                Add(diagnostics, "empty-choice-step", $"{stepPath}.choices", "A choice step must contain at least one choice.");

            if (step.AutoAdvanceAfter != null)
                Add(diagnostics, "auto-advance-on-choice", $"{stepPath}.autoAdvanceAfter", "A choice step cannot auto-advance.");

            for (var choiceIndex = 0; choiceIndex < step.Choices.Count; choiceIndex++)
            {
                var choice = step.Choices[choiceIndex];
                var choicePath = $"{stepPath}.choices[{choiceIndex}]";

                if (string.IsNullOrWhiteSpace(choice.Text.Id))
                    Add(diagnostics, "missing-choice-text", $"{choicePath}.text", "Choice text is required.");

                ValidateConditions(choice.Conditions, $"{choicePath}.conditions", prototypeManager, diagnostics);
                ValidateLocalizationArguments(choice.TextArgs, $"{choicePath}.textArgs", prototypeManager, participants, diagnostics);
                ValidateActions(choice.Actions, $"{choicePath}.actions", prototypeManager, participants, diagnostics);
                ValidateRootStepReference(choice.NextStep, $"{choicePath}.nextStep", rootStepIndices, diagnostics);

                if (choice.NextDialogue != null && !string.IsNullOrWhiteSpace(choice.NextStep))
                {
                    Add(
                        diagnostics,
                        "multiple-choice-destinations",
                        choicePath,
                        "A choice cannot use both nextDialogue and nextStep.");
                }

                if (choice.NextDialogue is { } nextDialogue
                    && !prototypeManager.HasIndex<DialoguePrototype>(nextDialogue))
                {
                    Add(
                        diagnostics,
                        "missing-next-dialogue",
                        $"{choicePath}.nextDialogue",
                        $"Referenced dialogue '{nextDialogue}' does not exist.");
                }

                if (choice.ResponseSteps.Count > 0)
                {
                    ValidateSteps(
                        choice.ResponseSteps,
                        $"{choicePath}.responseSteps",
                        rootStepIndices,
                        prototypeManager,
                        participants,
                        diagnostics);
                }

                if (choice.FailureResponseSteps.Count > 0)
                {
                    ValidateSteps(
                        choice.FailureResponseSteps,
                        $"{choicePath}.failureResponseSteps",
                        rootStepIndices,
                        prototypeManager,
                        participants,
                        diagnostics);
                }
            }
        }
    }

    private static void ValidateRequirementActionPlacement(
        IReadOnlyList<DialogueActionPrototype> actions,
        string path,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            if (!DialogueActionRequirementSystem.IsRequirementAction(actions[i].Type))
                continue;

            Add(
                diagnostics,
                "requirement-action-outside-choice",
                $"{path}[{i}].type",
                "Item, bank, and access transactions are allowed only in a choice's actions so failure is handled safely.");
        }
    }

    private static void ValidateStepPresentation(
        DialogueStep step,
        string path,
        IReadOnlyDictionary<string, DialogueParticipantPrototype> participants,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        var speakerId = string.IsNullOrWhiteSpace(step.SpeakerId)
            ? (step.Speaker == DialogueSpeaker.Initiator ? "initiator" : "npc")
            : step.SpeakerId;

        ValidateParticipantReference(speakerId, $"{path}.speakerId", participants, diagnostics);
        ValidateParticipantReference(step.LeftActor, $"{path}.leftActor", participants, diagnostics);
        ValidateParticipantReference(step.RightActor, $"{path}.rightActor", participants, diagnostics);

        if (!string.IsNullOrWhiteSpace(step.LeftActor)
            && step.LeftActor == step.RightActor)
        {
            Add(
                diagnostics,
                "duplicate-stage-actor",
                path,
                "The same participant cannot occupy both portrait slots in one step.");
        }

        if (!string.IsNullOrWhiteSpace(step.Expression))
            ValidateExpression(speakerId, step.Expression, $"{path}.expression", participants, diagnostics);

        foreach (var (participantId, expression) in step.Expressions)
        {
            ValidateExpression(
                participantId,
                expression,
                $"{path}.expressions.{participantId}",
                participants,
                diagnostics);
        }
    }

    private static void ValidateExpression(
        string participantId,
        string expression,
        string path,
        IReadOnlyDictionary<string, DialogueParticipantPrototype> participants,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            Add(diagnostics, "missing-expression-id", path, "Expression ids cannot be empty.");
            return;
        }

        if (!participants.TryGetValue(participantId, out var participant))
        {
            Add(
                diagnostics,
                "expression-on-unconfigured-participant",
                path,
                $"Participant '{participantId}' has no expression portraits. Define it in participants first.");
            return;
        }

        if (!participant.Expressions.ContainsKey(expression))
        {
            Add(
                diagnostics,
                "missing-participant-expression",
                path,
                $"Participant '{participantId}' has no expression '{expression}'.");
        }
    }

    private static void ValidateParticipantReference(
        string? participantId,
        string path,
        IReadOnlyDictionary<string, DialogueParticipantPrototype> participants,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(participantId)
            || participantId is "initiator" or "npc"
            || participants.ContainsKey(participantId))
        {
            return;
        }

        Add(
            diagnostics,
            "missing-dialogue-participant",
            path,
            $"Participant '{participantId}' is not defined by this dialogue.");
    }

    private static void ValidateLocalizationArguments(
        IReadOnlyList<DialogueLocArgumentPrototype> arguments,
        string path,
        IPrototypeManager prototypeManager,
        IReadOnlyDictionary<string, DialogueParticipantPrototype> participants,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var argumentPath = $"{path}[{i}]";

            if (string.IsNullOrWhiteSpace(argument.Id))
            {
                Add(diagnostics, "missing-localization-argument-id", $"{argumentPath}.id", "Localization arguments require an id.");
                continue;
            }

            if (!ids.Add(argument.Id))
            {
                Add(
                    diagnostics,
                    "duplicate-localization-argument-id",
                    $"{argumentPath}.id",
                    $"Localization argument '{argument.Id}' is already configured for this string.");
            }

            switch (argument.Type)
            {
                case DialogueLocArgumentType.Literal when argument.Value == null:
                    Add(diagnostics, "missing-localization-literal", $"{argumentPath}.value", "Literal arguments require a value.");
                    break;
                case DialogueLocArgumentType.Counter when string.IsNullOrWhiteSpace(argument.Counter):
                    Add(diagnostics, "missing-localization-counter", $"{argumentPath}.counter", "Counter arguments require a counter name.");
                    break;
                case DialogueLocArgumentType.ParticipantName:
                    if (string.IsNullOrWhiteSpace(argument.Participant))
                    {
                        Add(diagnostics, "missing-localization-participant", $"{argumentPath}.participant", "ParticipantName requires a participant id.");
                    }
                    else
                    {
                        ValidateParticipantReference(argument.Participant, $"{argumentPath}.participant", participants, diagnostics);
                    }

                    break;
                case DialogueLocArgumentType.PrototypeName:
                    ValidatePortraitPrototype(argument.Prototype, $"{argumentPath}.prototype", prototypeManager, diagnostics);
                    break;
            }
        }
    }

    private static void ValidatePortraitPrototype(
        EntProtoId? prototype,
        string path,
        IPrototypeManager prototypeManager,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (prototype == null)
        {
            Add(diagnostics, "missing-portrait-prototype", path, "A portrait reference requires an entity prototype.");
            return;
        }

        if (!prototypeManager.HasIndex<EntityPrototype>(prototype.Value))
        {
            Add(
                diagnostics,
                "missing-portrait-entity",
                path,
                $"Entity prototype '{prototype}' does not exist.");
        }
    }

    private static void ValidatePortraitPrototype(
        EntProtoId prototype,
        string path,
        IPrototypeManager prototypeManager,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (prototypeManager.HasIndex<EntityPrototype>(prototype))
            return;

        Add(
            diagnostics,
            "missing-portrait-entity",
            path,
            $"Entity prototype '{prototype}' does not exist.");
    }

    private static void ValidateConditions(
        IReadOnlyList<DialogueConditionPrototype> conditions,
        string path,
        IPrototypeManager prototypeManager,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        var flagValues = new Dictionary<string, bool>(StringComparer.Ordinal);
        var completedValues = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool? storeAvailable = null;
        var counterBounds = new Dictionary<string, CounterBounds>(StringComparer.Ordinal);

        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            var conditionPath = $"{path}[{i}]";

            switch (condition.Type)
            {
                case DialogueConditionType.Flag:
                    if (string.IsNullOrWhiteSpace(condition.Flag))
                    {
                        Add(diagnostics, "missing-condition-flag", $"{conditionPath}.flag", "Flag condition requires a flag name.");
                        break;
                    }

                    CheckBooleanContradiction(flagValues, condition.Flag, condition.Value, conditionPath, diagnostics);
                    break;
                case DialogueConditionType.CounterAtLeast:
                case DialogueConditionType.CounterAtMost:
                case DialogueConditionType.CounterEquals:
                    if (string.IsNullOrWhiteSpace(condition.Counter))
                    {
                        Add(
                            diagnostics,
                            "missing-condition-counter",
                            $"{conditionPath}.counter",
                            "Counter condition requires a counter name.");
                        break;
                    }

                    var bounds = counterBounds.GetValueOrDefault(condition.Counter);
                    bounds = condition.Type switch
                    {
                        DialogueConditionType.CounterAtLeast => bounds.WithMinimum(condition.Amount),
                        DialogueConditionType.CounterAtMost => bounds.WithMaximum(condition.Amount),
                        _ => bounds.WithExact(condition.Amount)
                    };
                    counterBounds[condition.Counter] = bounds;

                    if (!bounds.IsPossible)
                    {
                        Add(
                            diagnostics,
                            "contradictory-counter-conditions",
                            conditionPath,
                            $"Conditions for counter '{condition.Counter}' cannot be satisfied together.");
                    }

                    break;
                case DialogueConditionType.DialogueCompleted:
                    if (condition.Dialogue == null)
                    {
                        Add(
                            diagnostics,
                            "missing-condition-dialogue",
                            $"{conditionPath}.dialogue",
                            "DialogueCompleted condition requires a dialogue.");
                        break;
                    }

                    var dialogue = condition.Dialogue.Value;
                    if (!prototypeManager.HasIndex<DialoguePrototype>(dialogue))
                    {
                        Add(
                            diagnostics,
                            "missing-condition-dialogue-prototype",
                            $"{conditionPath}.dialogue",
                            $"Referenced dialogue '{dialogue}' does not exist.");
                    }

                    CheckBooleanContradiction(completedValues, dialogue.Id, condition.Value, conditionPath, diagnostics);
                    break;
                case DialogueConditionType.StoreAvailable:
                    if (storeAvailable != null && storeAvailable != condition.Value)
                    {
                        Add(
                            diagnostics,
                            "contradictory-store-conditions",
                            conditionPath,
                            "StoreAvailable conditions require both presence and absence of a store.");
                    }

                    storeAvailable = condition.Value;
                    break;
                case DialogueConditionType.ItemCountAtLeast:
                    ValidateItemCondition(condition, conditionPath, prototypeManager, diagnostics);
                    break;
                case DialogueConditionType.BankBalanceAtLeast:
                    if (condition.Amount <= 0)
                    {
                        Add(
                            diagnostics,
                            "invalid-bank-balance-amount",
                            $"{conditionPath}.amount",
                            "BankBalanceAtLeast requires an amount greater than zero.");
                    }

                    break;
            }
        }
    }

    private static void ValidateActions(
        IReadOnlyList<DialogueActionPrototype> actions,
        string path,
        IPrototypeManager prototypeManager,
        IReadOnlyDictionary<string, DialogueParticipantPrototype> participants,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            var actionPath = $"{path}[{i}]";

            ValidateLocalizationArguments(
                action.MessageArgs,
                $"{actionPath}.messageArgs",
                prototypeManager,
                participants,
                diagnostics);

            switch (action.Type)
            {
                case DialogueActionType.GiveItem:
                    if (action.Prototype == null)
                    {
                        Add(diagnostics, "missing-give-item-prototype", $"{actionPath}.prototype", "GiveItem requires a prototype.");
                    }
                    else if (!prototypeManager.HasIndex<EntityPrototype>(action.Prototype.Value))
                    {
                        Add(
                            diagnostics,
                            "missing-give-item-entity",
                            $"{actionPath}.prototype",
                            $"Entity prototype '{action.Prototype}' does not exist.");
                    }

                    break;
                case DialogueActionType.TakeItem:
                    if (action.Prototype == null)
                    {
                        Add(diagnostics, "missing-take-item-prototype", $"{actionPath}.prototype", "TakeItem requires a prototype.");
                    }
                    else if (!prototypeManager.HasIndex<EntityPrototype>(action.Prototype.Value))
                    {
                        Add(
                            diagnostics,
                            "missing-take-item-entity",
                            $"{actionPath}.prototype",
                            $"Entity prototype '{action.Prototype}' does not exist.");
                    }

                    if (action.Amount <= 0)
                    {
                        Add(diagnostics, "invalid-take-item-amount", $"{actionPath}.amount", "TakeItem requires an amount greater than zero.");
                    }

                    break;
                case DialogueActionType.DebitBankAccount or DialogueActionType.CreditBankAccount
                    when action.Amount <= 0:
                    Add(
                        diagnostics,
                        "invalid-bank-action-amount",
                        $"{actionPath}.amount",
                        $"{action.Type} requires an amount greater than zero.");
                    break;
                case DialogueActionType.AddAccess or DialogueActionType.RemoveAccess:
                    ValidateAccessAction(action, actionPath, prototypeManager, diagnostics);
                    break;
                case DialogueActionType.SendChat when string.IsNullOrWhiteSpace(action.Message.Id):
                    Add(diagnostics, "missing-chat-message", $"{actionPath}.message", "SendChat requires a localization id.");
                    break;
                case DialogueActionType.SetFlag when string.IsNullOrWhiteSpace(action.Flag):
                    Add(diagnostics, "missing-action-flag", $"{actionPath}.flag", "SetFlag requires a flag name.");
                    break;
                case DialogueActionType.AddCounter or DialogueActionType.SetCounter
                    when string.IsNullOrWhiteSpace(action.Counter):
                    Add(diagnostics, "missing-action-counter", $"{actionPath}.counter", $"{action.Type} requires a counter name.");
                    break;
                case DialogueActionType.MoveSpeakerToSpeaker:
                    ValidatePositiveFinite(action.Range, $"{actionPath}.range", "invalid-movement-range", diagnostics);
                    ValidateFinite(action.OffsetX, $"{actionPath}.offsetX", "invalid-movement-offset-x", diagnostics);
                    ValidateFinite(action.OffsetY, $"{actionPath}.offsetY", "invalid-movement-offset-y", diagnostics);
                    if (action.InRangeMaxSpeed is { } speed)
                        ValidateNonNegativeFinite(speed, $"{actionPath}.inRangeMaxSpeed", "invalid-movement-speed", diagnostics);
                    break;
                case DialogueActionType.RotateSpeakerRelative:
                    ValidateFinite(action.Degrees, $"{actionPath}.degrees", "invalid-rotation", diagnostics);
                    break;
            }

            if (DialogueActionRequirementSystem.IsRequirementAction(action.Type)
                && action.OnlyIfPreviousActionSucceeded)
            {
                Add(
                    diagnostics,
                    "conditional-requirement-action",
                    $"{actionPath}.onlyIfPreviousActionSucceeded",
                    "Required payment, bank, and access actions cannot be conditional because a choice could continue without applying them.");
            }
        }
    }

    private static void ValidateAccessAction(
        DialogueActionPrototype action,
        string actionPath,
        IPrototypeManager prototypeManager,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (action.Accesses.Count == 0)
        {
            Add(
                diagnostics,
                "missing-dialogue-accesses",
                $"{actionPath}.accesses",
                $"{action.Type} requires at least one access level.");
            return;
        }

        for (var i = 0; i < action.Accesses.Count; i++)
        {
            var access = action.Accesses[i];
            if (prototypeManager.HasIndex<AccessLevelPrototype>(access))
                continue;

            Add(
                diagnostics,
                "missing-dialogue-access-prototype",
                $"{actionPath}.accesses[{i}]",
                $"Access level '{access}' does not exist.");
        }
    }

    private static void ValidateItemCondition(
        DialogueConditionPrototype condition,
        string conditionPath,
        IPrototypeManager prototypeManager,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (condition.Prototype == null)
        {
            Add(
                diagnostics,
                "missing-condition-item-prototype",
                $"{conditionPath}.prototype",
                "ItemCountAtLeast requires an entity prototype.");
        }
        else if (!prototypeManager.HasIndex<EntityPrototype>(condition.Prototype.Value))
        {
            Add(
                diagnostics,
                "missing-condition-item-entity",
                $"{conditionPath}.prototype",
                $"Entity prototype '{condition.Prototype}' does not exist.");
        }

        if (condition.Amount <= 0)
        {
            Add(
                diagnostics,
                "invalid-condition-item-amount",
                $"{conditionPath}.amount",
                "ItemCountAtLeast requires an amount greater than zero.");
        }
    }

    private static void ValidateMusic(
        DialogueMusicCuePrototype? music,
        string path,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (music == null)
            return;

        ValidateNonNegativeFinite(music.FadeIn, $"{path}.fadeIn", "invalid-music-fade-in", diagnostics);
        ValidateNonNegativeFinite(music.FadeOut, $"{path}.fadeOut", "invalid-music-fade-out", diagnostics);

        if (!music.Stop && music.Sound == null)
            Add(diagnostics, "missing-music-sound", $"{path}.sound", "A music cue must provide sound unless stop is true.");
    }

    private static void ValidatePersonalSteps(
        IReadOnlyList<DialogueStep> steps,
        string path,
        IPrototypeManager prototypeManager,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var stepPath = $"{path}[{i}]";
            ValidatePersonalActions(step.Actions, $"{stepPath}.actions", diagnostics);

            for (var choiceIndex = 0; choiceIndex < step.Choices.Count; choiceIndex++)
            {
                var choice = step.Choices[choiceIndex];
                var choicePath = $"{stepPath}.choices[{choiceIndex}]";
                ValidatePersonalActions(choice.Actions, $"{choicePath}.actions", diagnostics);

                if (choice.NextDialogue is { } nextDialogue
                    && prototypeManager.TryIndex<DialoguePrototype>(nextDialogue, out var destination)
                    && destination.InteractionMode == DialogueInteractionMode.SharedWorld)
                {
                    Add(
                        diagnostics,
                        "personal-to-shared-transition",
                        $"{choicePath}.nextDialogue",
                        "A personal dialogue cannot transition to a shared-world dialogue because other personal sessions may be active.");
                }

                ValidatePersonalSteps(
                    choice.ResponseSteps,
                    $"{choicePath}.responseSteps",
                    prototypeManager,
                    diagnostics);
                ValidatePersonalSteps(
                    choice.FailureResponseSteps,
                    $"{choicePath}.failureResponseSteps",
                    prototypeManager,
                    diagnostics);
            }
        }
    }

    private static void ValidatePersonalActions(
        IReadOnlyList<DialogueActionPrototype> actions,
        string path,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action.Type is not (DialogueActionType.MoveSpeakerToSpeaker
                or DialogueActionType.SleepSpeakerAi
                or DialogueActionType.WakeSpeakerAi))
            {
                continue;
            }

            Add(
                diagnostics,
                "shared-world-action-in-personal-dialogue",
                $"{path}[{i}]",
                $"Action '{action.Type}' cannot run in a personal dialogue because it changes shared NPC control state.");
        }
    }

    private static void ValidateRootStepReference(
        string? stepId,
        string path,
        IReadOnlyDictionary<string, int> rootStepIndices,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(stepId) && !rootStepIndices.ContainsKey(stepId))
            Add(diagnostics, "missing-next-step", path, $"Root step id '{stepId}' does not exist.");
    }

    private static void CheckBooleanContradiction(
        IDictionary<string, bool> values,
        string key,
        bool value,
        string path,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (values.TryGetValue(key, out var existing) && existing != value)
        {
            Add(
                diagnostics,
                "contradictory-boolean-conditions",
                path,
                $"Conditions for '{key}' require both true and false.");
        }

        values[key] = value;
    }

    private static void ValidatePositiveFinite(
        float value,
        string path,
        string code,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (!IsFinite(value) || value <= 0f)
            Add(diagnostics, code, path, "Value must be finite and greater than zero.");
    }

    private static void ValidateNonNegativeFinite(
        float value,
        string path,
        string code,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (!IsFinite(value) || value < 0f)
            Add(diagnostics, code, path, "Value must be finite and non-negative.");
    }

    private static void ValidateFinite(
        float value,
        string path,
        string code,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (!IsFinite(value))
            Add(diagnostics, code, path, "Value must be finite.");
    }

    private static void ValidateRange(
        float value,
        float minimum,
        float maximum,
        string path,
        string code,
        ICollection<DialoguePrototypeDiagnostic> diagnostics)
    {
        if (!IsFinite(value) || value < minimum || value > maximum)
            Add(diagnostics, code, path, $"Value must be finite and between {minimum} and {maximum}.");
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void Add(
        ICollection<DialoguePrototypeDiagnostic> diagnostics,
        string code,
        string path,
        string message)
    {
        diagnostics.Add(new DialoguePrototypeDiagnostic(code, path, message));
    }

    private readonly record struct CounterBounds(int? Minimum, int? Maximum, int? Exact, bool Impossible)
    {
        public bool IsPossible => !Impossible && (Exact is { } exact
            ? (Minimum == null || exact >= Minimum) && (Maximum == null || exact <= Maximum)
            : Minimum == null || Maximum == null || Minimum <= Maximum);

        public CounterBounds WithMinimum(int minimum)
        {
            return this with { Minimum = Minimum is { } current ? Math.Max(current, minimum) : minimum };
        }

        public CounterBounds WithMaximum(int maximum)
        {
            return this with { Maximum = Maximum is { } current ? Math.Min(current, maximum) : maximum };
        }

        public CounterBounds WithExact(int exact)
        {
            return this with
            {
                Exact = exact,
                Impossible = Impossible || Exact is { } current && current != exact
            };
        }
    }
}

public sealed class DialoguePrototypeValidationResult
{
    public IReadOnlyDictionary<string, int> RootStepIndices { get; }
    public IReadOnlyList<DialoguePrototypeDiagnostic> Diagnostics { get; }
    public bool IsValid => Diagnostics.Count == 0;

    public DialoguePrototypeValidationResult(
        IReadOnlyDictionary<string, int> rootStepIndices,
        IReadOnlyList<DialoguePrototypeDiagnostic> diagnostics)
    {
        RootStepIndices = rootStepIndices;
        Diagnostics = diagnostics;
    }
}

public sealed record DialoguePrototypeDiagnostic(string Code, string Path, string Message);
