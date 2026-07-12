using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._WH40K.Dialogue.UI;
using Content.IntegrationTests.Utility;
using Content.Server._NF.Bank;
using Content.Server.Database;
using Content.Server.Hands.Systems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._WH40K.Dialogue;
using Content.Server._WH40K.Dialogue.Components;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Inventory;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Preferences;
using Content.Shared.PDA;
using Content.Shared.Stacks;
using Content.Shared._WH40K.Dialogue;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._WH40K.Dialogue;

[TestFixture]
[TestOf(typeof(DialoguePersistentMemoryFilter))]
[TestOf(typeof(DialogueItemSystem))]
[TestOf(typeof(DialogueAccessSystem))]
[TestOf(typeof(DialogueActionRequirementSystem))]
public sealed class DialoguePersistenceTests
{
    private static readonly ProtoId<DialoguePrototype> FizzoBaseDialogue = "WH40KFizzoBase";
    private static readonly ProtoId<DialoguePrototype> FizzoIntroDialogue = "WH40KFizzoIntro";
    private static readonly ProtoId<DialoguePrototype> FizzoRumorsDialogue = "WH40KFizzoRumors";

    [Test]
    public void DefaultSceneMatchesCanonicalLayout()
    {
        var prototype = new DialoguePrototype();
        var scene = prototype.Scene;

        Assert.Multiple(() =>
        {
            Assert.That(prototype.TypewriterCps, Is.EqualTo(30f));
            Assert.That(scene.HideHud, Is.True);
            Assert.That(scene.DimOpacity, Is.EqualTo(0.38f));
            Assert.That(scene.WindowWidth, Is.EqualTo(1200f));
            Assert.That(scene.WindowMinHeight, Is.EqualTo(200f));
            Assert.That(scene.WindowMaxHeight, Is.EqualTo(300f));
            Assert.That(scene.WindowAnchor, Is.EqualTo(DialogueWindowAnchor.BottomCenter));
            Assert.That(scene.WindowMargin, Is.EqualTo(48f));
            Assert.That(scene.ShowActors, Is.True);
            Assert.That(scene.InitiatorSide, Is.EqualTo(DialogueActorSide.Left));
            Assert.That(scene.NpcSide, Is.EqualTo(DialogueActorSide.Right));
            Assert.That(scene.DimInactiveActors, Is.True);
            Assert.That(scene.InactiveActorOpacity, Is.EqualTo(0.76f));
            Assert.That(scene.ActorScale, Is.EqualTo(20f));
            Assert.That(scene.ActorWidth, Is.EqualTo(540f));
            Assert.That(scene.ActorHeight, Is.EqualTo(1000f));
            Assert.That(scene.ActorGap, Is.EqualTo(360f));
            Assert.That(scene.ActorOverlap, Is.EqualTo(475f));
            Assert.That(scene.ActorWindowOverlap, Is.EqualTo(40f));
            Assert.That(scene.ActorStageOffsetY, Is.Zero);
            Assert.That(scene.LeftActorAlignmentX, Is.EqualTo(0.5f));
            Assert.That(scene.RightActorAlignmentX, Is.EqualTo(0.5f));
            Assert.That(scene.LeftActorOffsetX, Is.EqualTo(-350f));
            Assert.That(scene.LeftActorOffsetY, Is.EqualTo(200f));
            Assert.That(scene.RightActorOffsetX, Is.EqualTo(350f));
            Assert.That(scene.RightActorOffsetY, Is.EqualTo(200f));
            Assert.That(scene.SpeakerFontSize, Is.EqualTo(22));
            Assert.That(scene.BodyFontSize, Is.EqualTo(18));
            Assert.That(scene.ContinueFontSize, Is.EqualTo(18));
        });
    }

    [Test]
    public async Task DialogueInputLockBlocksAndRestoresMovement()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var entity = server.EntMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            var actionBlocker = server.System<ActionBlockerSystem>();

            Assert.That(actionBlocker.CanMove(entity), Is.True);

            server.EntMan.AddComponent<DialogueInputLockComponent>(entity);
            Assert.That(actionBlocker.CanMove(entity), Is.False);

            server.EntMan.RemoveComponent<DialogueInputLockComponent>(entity);
            Assert.That(actionBlocker.CanMove(entity), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DialogueClientOverlayOpensAndCloses()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false
        });
        var server = pair.Server;
        var client = pair.Client;
        var uiManager = client.ResolveDependency<IUserInterfaceManager>();
        var playerManager = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        EntityUid target = default;

        await server.WaitAssertion(() =>
        {
            var player = playerManager.Sessions.Single().AttachedEntity;
            Assert.That(player, Is.Not.Null);

            target = server.EntMan.SpawnEntity(
                "MobHumanWH40KDialogueParallelDummy",
                server.System<SharedTransformSystem>().GetMapCoordinates(player!.Value));
            server.EntMan.EventBus.RaiseLocalEvent(target, new InteractHandEvent(player.Value, target));
        });

        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var overlay = FindDialogueOverlay(uiManager.ActiveScreen);
            Assert.That(overlay, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(overlay!.Visible, Is.True);
                Assert.That(overlay.Parent, Is.Not.Null);
            });
        });

        await server.WaitAssertion(() =>
        {
            var player = playerManager.Sessions.Single().AttachedEntity;
            Assert.That(player, Is.Not.Null);
            var mobState = server.EntMan.GetComponent<MobStateComponent>(player!.Value);
            server.System<MobStateSystem>().ChangeMobState(player.Value, MobState.Dead, mobState);
        });

        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var overlay = FindDialogueOverlay(uiManager.ActiveScreen);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay!.Visible, Is.False);
        });

        await pair.CleanReturnAsync();

        static DialogueOverlay FindDialogueOverlay(Control control)
        {
            if (control == null)
                return null!;
            if (control is DialogueOverlay overlay)
                return overlay;

            foreach (var child in control.Children)
            {
                var result = FindDialogueOverlay(child);
                if (result != null)
                    return result;
            }

            return null;
        }
    }

    [Test]
    public async Task TransactionChoiceWithoutRefusalClosesDialogue()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false
        });
        var server = pair.Server;
        var client = pair.Client;
        var playerManager = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var net = client.ResolveDependency<IEntityNetworkManager>();
        var sessionId = 0;
        EntityUid target = default;

        EventHandler<object> onSystemMessage = (_, message) =>
        {
            if (message is DialogueOpenEvent opened)
                sessionId = opened.SessionId;
        };

        await client.WaitPost(() => net.ReceivedSystemMessage += onSystemMessage);

        try
        {
            await server.WaitAssertion(() =>
            {
                var player = playerManager.Sessions.Single().AttachedEntity;
                Assert.That(player, Is.Not.Null);

                target = server.EntMan.SpawnEntity(
                    "MobHumanWH40KDialogueRequirementFallbackDummy",
                    server.System<SharedTransformSystem>().GetMapCoordinates(player!.Value));
                server.EntMan.EventBus.RaiseLocalEvent(target, new InteractHandEvent(player.Value, target));
            });

            await pair.RunTicksSync(5);

            await client.WaitAssertion(() => Assert.That(sessionId, Is.GreaterThan(0)));
            await client.WaitPost(() => net.SendSystemNetworkMessage(new DialogueChoiceRequestEvent(sessionId, 0)));
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                Assert.That(server.EntMan.HasComponent<DialogueConversationComponent>(target), Is.False,
                    "A failed transaction without failureResponseSteps must close instead of advancing the dialogue.");
            });
        }
        finally
        {
            await client.WaitPost(() => net.ReceivedSystemMessage -= onSystemMessage);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PersonalSessionsShareNpcLeaseUntilLastDialogueEnds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var sessions = await server.AddDummySessions(2);
        EntityUid firstPlayer = default;
        EntityUid secondPlayer = default;
        EntityUid target = default;

        await server.WaitAssertion(() =>
        {
            firstPlayer = server.EntMan.SpawnEntity("WH40KDialoguePlayerDummy", map.MapCoords);
            secondPlayer = server.EntMan.SpawnEntity("WH40KDialoguePlayerDummy", map.MapCoords);
            target = server.EntMan.SpawnEntity("MobHumanWH40KDialogueParallelDummy", map.MapCoords);
            var htn = server.EntMan.GetComponent<HTNComponent>(target);
            var npc = server.System<NPCSystem>();
            npc.WakeNPC(target, htn);
            Assert.That(npc.IsAwake(target, htn), Is.True);
            server.PlayerMan.SetAttachedEntity(sessions[0], firstPlayer);
            server.PlayerMan.SetAttachedEntity(sessions[1], secondPlayer);

            server.EntMan.EventBus.RaiseLocalEvent(target, new InteractHandEvent(firstPlayer, target));
            server.EntMan.EventBus.RaiseLocalEvent(target, new InteractHandEvent(secondPlayer, target));

            var conversation = server.EntMan.GetComponent<DialogueConversationComponent>(target);
            Assert.Multiple(() =>
            {
                Assert.That(conversation.ActiveSessions, Is.EqualTo(2));
                Assert.That(conversation.HasSharedWorldSession, Is.False);
                Assert.That(server.EntMan.HasComponent<GodmodeComponent>(target), Is.True);
                Assert.That(npc.IsAwake(target, htn), Is.False);
            });
        });

        await server.WaitAssertion(() =>
        {
            var mobState = server.EntMan.GetComponent<MobStateComponent>(firstPlayer);
            server.System<MobStateSystem>().ChangeMobState(firstPlayer, MobState.Dead, mobState);

            var conversation = server.EntMan.GetComponent<DialogueConversationComponent>(target);
            var htn = server.EntMan.GetComponent<HTNComponent>(target);
            Assert.Multiple(() =>
            {
                Assert.That(conversation.ActiveSessions, Is.EqualTo(1));
                Assert.That(server.EntMan.HasComponent<GodmodeComponent>(target), Is.True,
                    "The first closing session must not release protection owned by the second session.");
                Assert.That(server.System<NPCSystem>().IsAwake(target, htn), Is.False,
                    "The first closing session must not wake AI while another dialogue is active.");
            });
        });

        await server.WaitAssertion(() =>
        {
            var mobState = server.EntMan.GetComponent<MobStateComponent>(secondPlayer);
            server.System<MobStateSystem>().ChangeMobState(secondPlayer, MobState.Dead, mobState);

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<DialogueConversationComponent>(target), Is.False);
                Assert.That(server.EntMan.HasComponent<GodmodeComponent>(target), Is.False,
                    "NPC protection must be released after the last dialogue ends.");
                Assert.That(
                    server.System<NPCSystem>().IsAwake(target, server.EntMan.GetComponent<HTNComponent>(target)),
                    Is.True,
                    "NPC AI must return to its original awake state after the last dialogue ends.");
            });
        });

        await server.WaitRunTicks(5);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SharedWorldSessionRemainsExclusive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var sessions = await server.AddDummySessions(2);
        EntityUid firstPlayer = default;
        EntityUid secondPlayer = default;
        EntityUid target = default;

        await server.WaitAssertion(() =>
        {
            firstPlayer = server.EntMan.SpawnEntity("WH40KDialoguePlayerDummy", map.MapCoords);
            secondPlayer = server.EntMan.SpawnEntity("WH40KDialoguePlayerDummy", map.MapCoords);
            target = server.EntMan.SpawnEntity("MobHumanWH40KDialogueSharedWorldDummy", map.MapCoords);
            server.PlayerMan.SetAttachedEntity(sessions[0], firstPlayer);
            server.PlayerMan.SetAttachedEntity(sessions[1], secondPlayer);

            server.EntMan.EventBus.RaiseLocalEvent(target, new InteractHandEvent(firstPlayer, target));
            server.EntMan.EventBus.RaiseLocalEvent(target, new InteractHandEvent(secondPlayer, target));

            var conversation = server.EntMan.GetComponent<DialogueConversationComponent>(target);
            Assert.Multiple(() =>
            {
                Assert.That(conversation.ActiveSessions, Is.EqualTo(1));
                Assert.That(conversation.HasSharedWorldSession, Is.True);
            });
        });

        await server.WaitAssertion(() =>
        {
            var mobState = server.EntMan.GetComponent<MobStateComponent>(firstPlayer);
            server.System<MobStateSystem>().ChangeMobState(firstPlayer, MobState.Dead, mobState);
            server.EntMan.EventBus.RaiseLocalEvent(target, new InteractHandEvent(secondPlayer, target));

            var conversation = server.EntMan.GetComponent<DialogueConversationComponent>(target);
            Assert.Multiple(() =>
            {
                Assert.That(conversation.ActiveSessions, Is.EqualTo(1));
                Assert.That(conversation.HasSharedWorldSession, Is.True);
                Assert.That(server.EntMan.HasComponent<GodmodeComponent>(target), Is.True);
            });
        });

        await server.WaitAssertion(() =>
        {
            var mobState = server.EntMan.GetComponent<MobStateComponent>(secondPlayer);
            server.System<MobStateSystem>().ChangeMobState(secondPlayer, MobState.Dead, mobState);
            Assert.That(server.EntMan.HasComponent<DialogueConversationComponent>(target), Is.False);
        });

        await server.WaitRunTicks(5);

        await pair.CleanReturnAsync();
    }

    [Test]
    public void SelectedPersistenceOnlyStoresConfiguredValues()
    {
        var interactable = new DialogueInteractableComponent
        {
            PersistenceMode = DialogueMemoryPersistenceMode.Selected
        };
        interactable.PersistentFlags.Add("intro_complete");
        interactable.PersistentCompletedDialogues.Add("intro_dialogue");

        var source = new DialoguePlayerMemory
        {
            Flags = new HashSet<string> { "intro_complete", "temporary_flag" },
            Counters = new Dictionary<string, int> { ["temporary_counter"] = 3 },
            CompletedDialogues = new HashSet<string> { "intro_dialogue", "temporary_dialogue" }
        };

        var persisted = DialoguePersistentMemoryFilter.Create(source, interactable);
        var restored = new DialoguePlayerMemory();
        DialoguePersistentMemoryFilter.Load(restored, persisted, interactable);

        Assert.Multiple(() =>
        {
            Assert.That(persisted.Flags, Is.EquivalentTo(new[] { "intro_complete" }));
            Assert.That(persisted.Counters, Is.Empty);
            Assert.That(persisted.CompletedDialogues, Is.EquivalentTo(new[] { "intro_dialogue" }));
            Assert.That(restored.Flags, Is.EquivalentTo(new[] { "intro_complete" }));
            Assert.That(restored.Counters, Is.Empty);
            Assert.That(restored.CompletedDialogues, Is.EquivalentTo(new[] { "intro_dialogue" }));
        });
    }

    [Test]
    public void AllPersistenceRetainsEverySemanticValue()
    {
        var interactable = new DialogueInteractableComponent();
        var source = new DialoguePlayerMemory
        {
            Flags = new HashSet<string> { "story_flag" },
            Counters = new Dictionary<string, int> { ["whiskey"] = 4 },
            CompletedDialogues = new HashSet<string> { "story_dialogue" }
        };

        var persisted = DialoguePersistentMemoryFilter.Create(source, interactable);
        var restored = new DialoguePlayerMemory();
        DialoguePersistentMemoryFilter.Load(restored, persisted, interactable);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Flags, Is.EquivalentTo(source.Flags));
            Assert.That(restored.Counters, Is.EquivalentTo(source.Counters));
            Assert.That(restored.CompletedDialogues, Is.EquivalentTo(source.CompletedDialogues));
        });
    }

    [Test]
    public async Task LoadedDialoguePrototypesPassSemanticValidation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var failures = server.ProtoMan
                .EnumeratePrototypes<DialoguePrototype>()
                .Select(prototype => (prototype.ID, Result: DialoguePrototypeValidator.Validate(prototype, server.ProtoMan)))
                .Where(entry => !entry.Result.IsValid)
                .SelectMany(entry => entry.Result.Diagnostics.Select(diagnostic =>
                    $"{entry.ID}: [{diagnostic.Code}] {diagnostic.Path}: {diagnostic.Message}"))
                .ToArray();

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ValidatorReportsAllNestedAuthoringErrorsWithPaths()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototype = new DialoguePrototype();
            prototype.Steps.Add(new DialogueStep
            {
                Type = DialogueStepType.Choice,
                Speaker = DialogueSpeaker.Npc,
                NextStep = "missing-step",
                Choices =
                [
                    new DialogueChoiceOptionPrototype
                    {
                        Conditions =
                        [
                            new DialogueConditionPrototype
                            {
                                Type = DialogueConditionType.CounterAtLeast
                            },
                            new DialogueConditionPrototype
                            {
                                Type = DialogueConditionType.ItemCountAtLeast,
                                Amount = 0
                            },
                            new DialogueConditionPrototype
                            {
                                Type = DialogueConditionType.BankBalanceAtLeast,
                                Amount = 0
                            }
                        ],
                        Actions =
                        [
                            new DialogueActionPrototype
                            {
                                Type = DialogueActionType.GiveItem
                            },
                            new DialogueActionPrototype
                            {
                                Type = DialogueActionType.TakeItem,
                                Amount = 0
                            },
                            new DialogueActionPrototype
                            {
                                Type = DialogueActionType.DebitBankAccount,
                                Amount = 0
                            },
                            new DialogueActionPrototype
                            {
                                Type = DialogueActionType.CreditBankAccount,
                                Amount = 1
                            }
                        ]
                    }
                ]
            });

            var result = DialoguePrototypeValidator.Validate(prototype, server.ProtoMan);
            var diagnostics = result.Diagnostics.ToDictionary(diagnostic => diagnostic.Code);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.False);
                Assert.That(diagnostics["missing-step-text"].Path, Is.EqualTo("steps[0].text"));
                Assert.That(diagnostics["missing-next-step"].Path, Is.EqualTo("steps[0].nextStep"));
                Assert.That(diagnostics["missing-choice-text"].Path, Is.EqualTo("steps[0].choices[0].text"));
                Assert.That(
                    diagnostics["missing-condition-counter"].Path,
                    Is.EqualTo("steps[0].choices[0].conditions[0].counter"));
                Assert.That(
                    diagnostics["missing-give-item-prototype"].Path,
                    Is.EqualTo("steps[0].choices[0].actions[0].prototype"));
                Assert.That(
                    diagnostics["missing-condition-item-prototype"].Path,
                    Is.EqualTo("steps[0].choices[0].conditions[1].prototype"));
                Assert.That(
                    diagnostics["invalid-condition-item-amount"].Path,
                    Is.EqualTo("steps[0].choices[0].conditions[1].amount"));
                Assert.That(
                    diagnostics["invalid-bank-balance-amount"].Path,
                    Is.EqualTo("steps[0].choices[0].conditions[2].amount"));
                Assert.That(
                    diagnostics["missing-take-item-prototype"].Path,
                    Is.EqualTo("steps[0].choices[0].actions[1].prototype"));
                Assert.That(
                    diagnostics["invalid-take-item-amount"].Path,
                    Is.EqualTo("steps[0].choices[0].actions[1].amount"));
                Assert.That(
                    diagnostics["invalid-bank-action-amount"].Path,
                    Is.EqualTo("steps[0].choices[0].actions[2].amount"));
                Assert.That(diagnostics.ContainsKey("missing-choice-failure-response"), Is.False,
                    "A missing refusal branch is valid: the runtime closes the dialogue as its safe fallback.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ValidatorRejectsTransactionalActionsOutsideChoices()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototype = new DialoguePrototype
            {
                StartActions =
                [
                    new DialogueActionPrototype
                    {
                        Type = DialogueActionType.DebitBankAccount,
                        Amount = 5
                    }
                ],
                CompleteActions =
                [
                    new DialogueActionPrototype
                    {
                        Type = DialogueActionType.CreditBankAccount,
                        Amount = 5
                    }
                ],
                Steps =
                [
                    new DialogueStep
                    {
                        Speaker = DialogueSpeaker.Npc,
                        Text = "heretek-dialogue-parallel-test-line",
                        Actions =
                        [
                            new DialogueActionPrototype
                            {
                                Type = DialogueActionType.TakeItem,
                                Prototype = "SpaceCash10",
                                Amount = 1
                            }
                        ]
                    }
                ]
            };

            var result = DialoguePrototypeValidator.Validate(prototype, server.ProtoMan);
            var paths = result.Diagnostics
                .Where(diagnostic => diagnostic.Code == "requirement-action-outside-choice")
                .Select(diagnostic => diagnostic.Path)
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.False);
                Assert.That(paths, Is.EquivalentTo(new[]
                {
                    "startActions[0].type",
                    "completeActions[0].type",
                    "steps[0].actions[0].type"
                }));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ValidatorSupportsNamedCastExpressionsAndLocalizationArguments()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototype = new DialoguePrototype();
            prototype.Participants.Add(new DialogueParticipantPrototype
            {
                Id = "scribe",
                Source = DialogueParticipantSource.Target,
                Name = "heretek-dialogue-parallel-test-line",
                Portrait = "MobHuman",
                Expressions =
                {
                    ["stern"] = "MobHuman"
                },
                Side = DialogueActorSide.Right
            });
            prototype.Participants.Add(new DialogueParticipantPrototype
            {
                Id = "vox",
                Name = "heretek-dialogue-parallel-test-line",
                Portrait = "MobHuman",
                Side = DialogueActorSide.Left
            });
            prototype.Steps.Add(new DialogueStep
            {
                Speaker = DialogueSpeaker.Npc,
                SpeakerId = "scribe",
                Expression = "stern",
                LeftActor = "vox",
                RightActor = "scribe",
                Text = "heretek-dialogue-parallel-test-line",
                TextArgs =
                [
                    new DialogueLocArgumentPrototype
                    {
                        Id = "item",
                        Type = DialogueLocArgumentType.PrototypeName,
                        Prototype = "DrinkWhiskeyGlass"
                    },
                    new DialogueLocArgumentPrototype
                    {
                        Id = "amount",
                        Type = DialogueLocArgumentType.Number,
                        Amount = 3
                    },
                    new DialogueLocArgumentPrototype
                    {
                        Id = "speaker",
                        Type = DialogueLocArgumentType.ParticipantName,
                        Participant = "scribe"
                    }
                ]
            });

            var result = DialoguePrototypeValidator.Validate(prototype, server.ProtoMan);
            Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ValidatorReportsCastAndLocalizationAuthoringErrors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototype = new DialoguePrototype();
            prototype.Participants.Add(new DialogueParticipantPrototype
            {
                Id = "scribe",
                Portrait = "MobHuman"
            });
            prototype.Steps.Add(new DialogueStep
            {
                Speaker = DialogueSpeaker.Npc,
                SpeakerId = "missing",
                Expression = "angry",
                LeftActor = "scribe",
                RightActor = "scribe",
                Text = "heretek-dialogue-parallel-test-line",
                TextArgs =
                [
                    new DialogueLocArgumentPrototype
                    {
                        Id = "item",
                        Type = DialogueLocArgumentType.PrototypeName
                    },
                    new DialogueLocArgumentPrototype
                    {
                        Id = "item",
                        Type = DialogueLocArgumentType.Counter
                    }
                ]
            });

            var result = DialoguePrototypeValidator.Validate(prototype, server.ProtoMan);
            var diagnostics = result.Diagnostics.ToDictionary(diagnostic => diagnostic.Code);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.False);
                Assert.That(diagnostics["missing-dialogue-participant"].Path, Is.EqualTo("steps[0].speakerId"));
                Assert.That(diagnostics["expression-on-unconfigured-participant"].Path, Is.EqualTo("steps[0].expression"));
                Assert.That(diagnostics["duplicate-stage-actor"].Path, Is.EqualTo("steps[0]"));
                Assert.That(diagnostics["missing-portrait-prototype"].Path, Is.EqualTo("steps[0].textArgs[0].prototype"));
                Assert.That(diagnostics["duplicate-localization-argument-id"].Path, Is.EqualTo("steps[0].textArgs[1].id"));
                Assert.That(diagnostics["missing-localization-counter"].Path, Is.EqualTo("steps[0].textArgs[1].counter"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ValidatorRejectsSharedNpcControlInPersonalDialogue()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototype = new DialoguePrototype
            {
                InteractionMode = DialogueInteractionMode.Personal
            };
            prototype.Steps.Add(new DialogueStep
            {
                Speaker = DialogueSpeaker.Npc,
                Text = "heretek-dialogue-parallel-test-line",
                Actions =
                [
                    new DialogueActionPrototype
                    {
                        Type = DialogueActionType.MoveSpeakerToSpeaker
                    },
                    new DialogueActionPrototype
                    {
                        Type = DialogueActionType.WakeSpeakerAi
                    }
                ]
            });

            var result = DialoguePrototypeValidator.Validate(prototype, server.ProtoMan);
            var concurrencyErrors = result.Diagnostics
                .Where(diagnostic => diagnostic.Code == "shared-world-action-in-personal-dialogue")
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(concurrencyErrors, Has.Length.EqualTo(2));
                Assert.That(concurrencyErrors[0].Path, Is.EqualTo("steps[0].actions[0]"));
                Assert.That(concurrencyErrors[1].Path, Is.EqualTo("steps[0].actions[1]"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FizzoPersistsIntroductionButNotWhiskeyCount()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var fizzo = server.EntMan.SpawnEntity("MobQueenChrysalisImperiumDialogueFizzo", MapCoordinates.Nullspace);
            var interactable = server.EntMan.GetComponent<DialogueInteractableComponent>(fizzo);
            var baseDialogue = server.ProtoMan.Index(FizzoBaseDialogue);
            var introDialogue = server.ProtoMan.Index(FizzoIntroDialogue);
            var rumorsDialogue = server.ProtoMan.Index(FizzoRumorsDialogue);
            var rewardChoice = introDialogue.Steps
                .Where(step => step.Type == DialogueStepType.Choice)
                .SelectMany(step => step.Choices)
                .Single(choice => choice.Text.Id == "heretek-dialogue-fizzo-intro-choice-4");
            var rewardCounter = rewardChoice.ResponseSteps
                .SelectMany(step => step.Actions)
                .Single(action => action.Type == DialogueActionType.AddCounter);

            Assert.Multiple(() =>
            {
                Assert.That(interactable.PersistenceMode, Is.EqualTo(DialogueMemoryPersistenceMode.Selected));
                Assert.That(interactable.PersistentFlags, Is.EquivalentTo(new[] { "wh40k_fizzo_intro_done" }));
                Assert.That(interactable.PersistentCounters, Is.Empty);
                Assert.That(interactable.PersistentCompletedDialogues, Is.Empty);
                Assert.That(interactable.BaseDialogue?.Id, Is.EqualTo(baseDialogue.ID));
                Assert.That(baseDialogue.InteractionMode, Is.EqualTo(DialogueInteractionMode.Personal));
                Assert.That(introDialogue.InteractionMode, Is.EqualTo(DialogueInteractionMode.Personal));
                Assert.That(rumorsDialogue.InteractionMode, Is.EqualTo(DialogueInteractionMode.Personal));
                Assert.That(
                    baseDialogue.Steps[0].Choices.Exists(choice => choice.NextDialogue?.Id == rumorsDialogue.ID),
                    Is.True,
                    "Fizzo's base hub must provide the rumors branch.");
                Assert.That(rumorsDialogue.Steps, Has.Count.EqualTo(3));
                Assert.That(rumorsDialogue.Steps[1].Type, Is.EqualTo(DialogueStepType.Choice));
                Assert.That(rumorsDialogue.Steps[1].Choices, Has.Count.EqualTo(4));
                Assert.That(rewardCounter.OnlyIfPreviousActionSucceeded, Is.True);
            });

            var courierChoices = rumorsDialogue.Steps[1].Choices
                .Where(choice => choice.Conditions.Any(condition =>
                    condition.Counter == "wh40k_fizzo_courier_stage"))
                .ToArray();
            Assert.That(courierChoices, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(courierChoices[0].Conditions[0].Type, Is.EqualTo(DialogueConditionType.CounterEquals));
                Assert.That(courierChoices[0].Actions, Has.Count.EqualTo(1));
                Assert.That(courierChoices[0].Actions[0].Type, Is.EqualTo(DialogueActionType.SetCounter));
                Assert.That(courierChoices[0].Actions[0].Amount, Is.EqualTo(1));
                Assert.That(courierChoices[1].Conditions[0].Type, Is.EqualTo(DialogueConditionType.CounterAtLeast));
            });

            Assert.That(interactable.Dialogues, Has.Count.EqualTo(1));
            var introduction = interactable.Dialogues[0];
            Assert.That(introduction.Dialogue?.Id, Is.EqualTo("WH40KFizzoIntro"));
            Assert.That(introduction.Conditions, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(introduction.Conditions[0].Type, Is.EqualTo(DialogueConditionType.Flag));
                Assert.That(introduction.Conditions[0].Flag, Is.EqualTo("wh40k_fizzo_intro_done"));
                Assert.That(introduction.Conditions[0].Value, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TakeItemsFromHandsConsumesStacksAtomically()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var player = server.EntMan.SpawnEntity("MobHuman", map.MapCoords);
            var cash = server.EntMan.SpawnEntity("SpaceCash10", map.MapCoords);
            var hands = server.System<HandsSystem>();
            var dialogueItems = server.System<DialogueItemSystem>();

            Assert.That(hands.TryPickupAnyHand(player, cash, checkActionBlocker: false), Is.True);
            Assert.That(dialogueItems.CountItems(player, DialogueItemSource.Hands, "SpaceCash10"), Is.EqualTo(10));
            Assert.That(dialogueItems.TryTakeItems(player, DialogueItemSource.Hands, "SpaceCash10", 6), Is.True);
            Assert.That(server.EntMan.GetComponent<StackComponent>(cash).Count, Is.EqualTo(4));

            Assert.That(dialogueItems.TryTakeItems(player, DialogueItemSource.Hands, "SpaceCash10", 5), Is.False);
            Assert.That(server.EntMan.GetComponent<StackComponent>(cash).Count, Is.EqualTo(4),
                "An incomplete payment must not consume part of a stack.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TakeItemsFromEquipmentDoesNotSearchHands()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var player = server.EntMan.SpawnEntity("MobHuman", map.MapCoords);
            var uniform = server.EntMan.SpawnEntity("ClothingUniformJumpsuitColorWhite", map.MapCoords);
            var inventory = server.System<InventorySystem>();
            var dialogueItems = server.System<DialogueItemSystem>();

            Assert.That(inventory.TryEquip(player, uniform, "jumpsuit", force: true), Is.True);
            Assert.That(dialogueItems.CountItems(player, DialogueItemSource.Hands, "ClothingUniformJumpsuitColorWhite"), Is.Zero);
            Assert.That(dialogueItems.TryTakeItems(player, DialogueItemSource.Hands, "ClothingUniformJumpsuitColorWhite", 1), Is.False);
            Assert.That(inventory.TryGetSlotEntity(player, "jumpsuit", out var equipped), Is.True);
            Assert.That(equipped, Is.EqualTo(uniform));

            Assert.That(dialogueItems.TryTakeItems(player, DialogueItemSource.Equipped, "ClothingUniformJumpsuitColorWhite", 1), Is.True);
            Assert.That(inventory.TryGetSlotEntity(player, "jumpsuit", out _), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TransactionRequirementsRejectMissingItemsWithoutChangingStacks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var player = server.EntMan.SpawnEntity("MobHuman", map.MapCoords);
            var cash = server.EntMan.SpawnEntity("SpaceCash10", map.MapCoords);
            var hands = server.System<HandsSystem>();
            var requirements = server.System<DialogueActionRequirementSystem>();

            Assert.That(hands.TryPickupAnyHand(player, cash, checkActionBlocker: false), Is.True);

            var payment = new DialogueActionPrototype
            {
                Type = DialogueActionType.TakeItem,
                Prototype = "SpaceCash10",
                Source = DialogueItemSource.Hands,
                Amount = 11
            };

            Assert.That(requirements.IsActionRequirementMet(player, payment), Is.False);
            Assert.That(server.EntMan.GetComponent<StackComponent>(cash).Count, Is.EqualTo(10));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AccessRequirementsRejectMissingCardAndMissingAccessWithoutMutation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var player = server.EntMan.SpawnEntity("MobHuman", map.MapCoords);
            var hands = server.System<HandsSystem>();
            var access = server.System<DialogueAccessSystem>();
            var requirements = server.System<DialogueActionRequirementSystem>();
            var maintenance = (ProtoId<AccessLevelPrototype>) "Maintenance";
            var engineering = (ProtoId<AccessLevelPrototype>) "Engineering";

            var addMaintenance = new DialogueActionPrototype
            {
                Type = DialogueActionType.AddAccess,
                AccessCardSource = DialogueAccessCardSource.Hands,
                Accesses = [maintenance]
            };

            Assert.That(requirements.IsActionRequirementMet(player, addMaintenance), Is.False,
                "A dialogue must not grant access when there is no eligible ID card.");

            var card = server.EntMan.SpawnEntity("PassengerIDCard", map.MapCoords);
            var cardAccess = server.EntMan.GetComponent<AccessComponent>(card);
            cardAccess.Tags.Clear();
            Assert.That(hands.TryPickupAnyHand(player, card, checkActionBlocker: false), Is.True);

            var removeMaintenance = new DialogueActionPrototype
            {
                Type = DialogueActionType.RemoveAccess,
                AccessCardSource = DialogueAccessCardSource.Hands,
                Accesses = [maintenance]
            };

            Assert.That(requirements.IsActionRequirementMet(player, removeMaintenance), Is.False);
            Assert.That(cardAccess.Tags, Is.Empty,
                "A failed access removal must not alter a card.");

            Assert.That(access.TryModifyAccess(player, DialogueAccessCardSource.Hands, addMaintenance.Accesses, add: true), Is.True);
            Assert.That(cardAccess.Tags, Does.Contain(maintenance));
            Assert.That(requirements.IsActionRequirementMet(player, removeMaintenance), Is.True);
            Assert.That(access.TryModifyAccess(player, DialogueAccessCardSource.Hands, removeMaintenance.Accesses, add: false), Is.True);
            Assert.That(cardAccess.Tags, Is.Empty);

            var pda = server.EntMan.SpawnEntity("PassengerPDA", map.MapCoords);
            var inventory = server.System<InventorySystem>();
            Assert.That(inventory.TryEquip(player, pda, "id", force: true), Is.True);
            var containedId = server.EntMan.GetComponent<PdaComponent>(pda).ContainedId;
            Assert.That(containedId, Is.Not.Null);

            Assert.That(access.TryModifyAccess(player, DialogueAccessCardSource.Pda, [engineering], add: true), Is.True);
            Assert.That(server.EntMan.GetComponent<AccessComponent>(containedId!.Value).Tags, Does.Contain(engineering));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScriptedBankCreditAndDebitUpdateTheCharacterProfile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var userId = server.PlayerMan.Sessions.Single().UserId;
        var preferences = server.ResolveDependency<Content.Server.Preferences.Managers.IServerPreferencesManager>();
        var current = (HumanoidCharacterProfile) preferences.GetPreferences(userId).SelectedCharacter;

        await server.WaitPost(() => preferences.SetProfile(
            userId,
            0,
            current.WithBankBalance(100),
            authoritative: true).Wait());

        await server.WaitAssertion(() =>
        {
            var session = server.PlayerMan.GetSessionById(userId);
            var player = server.EntMan.SpawnEntity("MobHuman", map.MapCoords);
            server.EntMan.AddComponent<BankAccountComponent>(player);
            var account = server.EntMan.GetComponent<BankAccountComponent>(player);
            var bank = server.System<BankSystem>();
            var requirements = server.System<DialogueActionRequirementSystem>();
            server.PlayerMan.SetAttachedEntity(session, player);

            Assert.That(account.Balance, Is.EqualTo(100));
            Assert.That(
                requirements.IsActionRequirementMet(player, new DialogueActionPrototype
                {
                    Type = DialogueActionType.DebitBankAccount,
                    Amount = 101
                }),
                Is.False,
                "An unaffordable dialogue debit must be rejected before the account is changed.");
            Assert.That(account.Balance, Is.EqualTo(100));
            Assert.That(bank.TryBankCredit(player, 25), Is.True);
            Assert.That(bank.TryBankWithdraw(player, 50), Is.True);
            Assert.That(bank.TryBankWithdraw(player, 76), Is.False);
            Assert.That(account.Balance, Is.EqualTo(75));
            Assert.That(
                ((HumanoidCharacterProfile) preferences.GetPreferences(userId).SelectedCharacter).BankBalance,
                Is.EqualTo(75));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ResetPersistentMemoryOverwritesStoredProgress()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var database = server.ResolveDependency<IServerDbManager>();
        var dialogue = server.System<DialogueSystem>();
        var userId = new NetUserId(Guid.NewGuid());
        const string memoryKey = "dialogue_test_reset";

        await database.SetDialoguePersistentMemoryAsync(
            userId,
            memoryKey,
            new DialoguePersistentMemoryData
            {
                Flags = ["saved_flag"],
                Counters = new Dictionary<string, int> { ["saved_counter"] = 2 },
                CompletedDialogues = ["saved_dialogue"]
            });

        Task resetTask = null!;
        await server.WaitPost(() => resetTask = dialogue.ResetPersistentMemoryAsync(userId, memoryKey));
        await resetTask;

        var reset = await database.GetDialoguePersistentMemoryAsync(userId, memoryKey);

        Assert.That(reset, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(reset!.Flags, Is.Empty);
            Assert.That(reset.Counters, Is.Empty);
            Assert.That(reset.CompletedDialogues, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
