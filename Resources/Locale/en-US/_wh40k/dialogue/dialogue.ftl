heretek-dialogue-verb-talk = Talk
heretek-dialogue-conversation-examine = [color=lightblue]This character is currently talking to { $count } { $count ->
    [one] person
   *[other] people
}.[/color]
heretek-dialogue-ui-raw-name = { $name }

heretek-dialogue-demo-001 = This time we are not only testing the scene itself, but the choice UI as well. Watch the window carefully: buttons will appear under the text, and nothing should overlap or collapse into a mess.
heretek-dialogue-demo-002 = If this survives long lines, music, and choice buttons at the same time, then the framework is finally starting to resemble a proper system.
heretek-dialogue-demo-003 = Where do you want to start with the first set of options?
heretek-dialogue-demo-004 = Now the window should keep the same rhythm and simply move on. The next set uses two buttons, and they must split evenly to the left and right of center.
heretek-dialogue-demo-004b = Now we test a short scene beat. After this line, the window will disappear, the NPC will look left, then right, then back at the player, laugh, and only then return the dialogue to the screen.
heretek-dialogue-demo-005 = Pick whichever of the two checks is more useful to you.
heretek-dialogue-demo-006 = As long as the buttons live on their own row, the body text must never sink into them. If the space is reserved in advance, the whole window stays calm instead of jumping when the line finishes.
heretek-dialogue-demo-007 = One last test remains: a single button. It must sit exactly in the center. Ready?
heretek-dialogue-demo-008 = If everything looked even just now, then the choices are already integrated into the scene UI instead of being slapped on top of it.
heretek-dialogue-demo-009 = After that, this mechanic can be moved into real story scenes with responses, inner thoughts, and actual consequences without embarrassment.
heretek-dialogue-demo-hidden-001 = { " " }
heretek-dialogue-parallel-test-line = Parallel dialogue test line.

heretek-dialogue-demo-choice-3a = Place the left option
heretek-dialogue-demo-choice-3a-selected = Let us check the left button first.
heretek-dialogue-demo-choice-3a-response = Good. If that button looks shifted or its text presses into the edges, then the centering is still wrong.

heretek-dialogue-demo-choice-3b = Keep the center active
heretek-dialogue-demo-choice-3b-selected = Then I will press the center option.
heretek-dialogue-demo-choice-3b-response = That is the best one for checking whether the middle of the selection truly sits on the window axis.

heretek-dialogue-demo-choice-3c = Check the right edge
heretek-dialogue-demo-choice-3c-selected = I want to see how the right option behaves.
heretek-dialogue-demo-choice-3c-response = Fair enough. The right button is usually the first to reveal a skew if the base point was not taken from the center.

heretek-dialogue-demo-choice-2a = Compare left and right
heretek-dialogue-demo-choice-2a-selected = First I will compare both sides against each other.
heretek-dialogue-demo-choice-2a-response = That is the most useful check. If one side feels visually heavier than the other, the problem becomes obvious immediately.

heretek-dialogue-demo-choice-2b = Check width headroom
heretek-dialogue-demo-choice-2b-selected = I would rather see whether the buttons still have comfortable width headroom.
heretek-dialogue-demo-choice-2b-response = The right approach. Even with a longer phrase, the text must not spill into the button frame or into the dialogue body.

heretek-dialogue-demo-choice-1a = Yes, finish the test
heretek-dialogue-demo-choice-1a-selected = Yes. Run the last case.
heretek-dialogue-demo-choice-1a-response = Excellent. A lone button in the middle instantly shows whether the base axis drifted left or right.
heretek-dialogue-demo-complete-chat = The first test is done. Now you can use me to verify state, counters, and server-side dialogue consequences.
heretek-dialogue-demo-loop-complete-chat = Dialogue complete. If you picked the reward path, the counter should already have changed.
heretek-dialogue-demo-close-chat = Closing the scene directly from a choice action. If the window vanished without an extra line, then closeDialogue worked correctly.

heretek-dialogue-state-001 = You have already seen the visual side. Now we test the server logic: flags, counters, item delivery, and switching between multiple dialogues on the same NPC.
heretek-dialogue-state-002 = If this behaves consistently, then the system is ready to be moved into a real scene instead of tormenting the same poor dummy forever.
heretek-dialogue-state-003 = Choose what exactly you want to verify on this pass.

heretek-dialogue-state-choice-1 = Give a whiskey glass
heretek-dialogue-state-choice-1-selected = Give me an item through a dialogue action.
heretek-dialogue-state-choice-1-response = Here. When you close this line, a whiskey glass should land in your hand and the NPC's internal counter should increase by one.

heretek-dialogue-state-choice-2 = Give nothing
heretek-dialogue-state-choice-2-selected = No reward this time, let us just verify a normal completion.
heretek-dialogue-state-choice-2-response = Also useful. The counter must stay unchanged, but the prototype completeActions should still run after the dialogue ends.

heretek-dialogue-state-choice-3 = Close immediately

heretek-dialogue-state-locked-001 = The limit has been reached. That means the dialogue selector saw the counter, switched to another prototype, and kept you out of the normal reward loop.
heretek-dialogue-state-locked-chat = That is enough for now. The selector hit the limit and used a chat-only branch without opening the scene window.

heretek-dialogue-fizzo-intro-001 = Welcome to the Ale Wrath. Sit closer to the counter, and try not to stare at the hooves. I pour cleaner with them than half this station does with hands, and I listen better than most priests.
heretek-dialogue-fizzo-intro-002 = A talking horse behind a bar. Space always finds a new way to remind me that normal was only a temporary agreement.
heretek-dialogue-fizzo-intro-003 = I want to stare, apologize, and order something strong all at once. I should probably start with whichever option embarrasses me least.
heretek-dialogue-fizzo-intro-003b = Relax. In this bar, oddities get a stool before they get a label.
heretek-dialogue-fizzo-intro-004 = How did I end up behind this counter? Simple. One day the station did not need a hero. It needed someone who could hold a glass steadier than other people's nerves.
heretek-dialogue-fizzo-intro-005 = She says it lazily, almost like a joke, but there is too much road and too many quiet disasters behind that ease for an ordinary bartender.
heretek-dialogue-fizzo-intro-006 = First I hauled crates. Then I listened to confessions. Then I realized a bar in orbit is a small ceasefire. People come here not because they are happy, but because they want one minute without having to keep their backs straight.
heretek-dialogue-fizzo-intro-007 = And you actually like that?
heretek-dialogue-fizzo-intro-008 = More than enough. A counter shows you exactly who is still standing out of stubbornness, who is doing it out of pride, and who is afraid that if they sit down, they will not get back up.
heretek-dialogue-fizzo-intro-008b = A good bartender does not really sell the bottle. She gives people brief permission to exhale and stop lying to themselves for a moment.
heretek-dialogue-fizzo-intro-009 = So let us skip ceremony. For a first conversation, I know only one decent ending, and it sounds like glass.
heretek-dialogue-fizzo-intro-010 = Taking the glass?
heretek-dialogue-fizzo-intro-011 = Here. Drink slowly, and do not argue with the music. It is always the first thing in here to know when someone needs another swallow of silence.

heretek-dialogue-fizzo-intro-choice-1 = A talking horse?
heretek-dialogue-fizzo-intro-choice-1-selected = Sorry, but I have to ask. Are you really a talking horse?
heretek-dialogue-fizzo-intro-choice-1-response = Formally, yes. In practice, I am a bartender, and that title sounds more dangerous around here than any diagnosis.

heretek-dialogue-fizzo-intro-choice-2 = Stay silent
heretek-dialogue-fizzo-intro-choice-2-selected = Better if I just listen for now.
heretek-dialogue-fizzo-intro-choice-2-response = That may be the healthiest reaction I have seen all day. Quiet people usually have either steel nerves or a very long biography.

heretek-dialogue-fizzo-intro-choice-3 = Pour me whiskey
heretek-dialogue-fizzo-intro-choice-3-selected = Pour me whiskey.
heretek-dialogue-fizzo-intro-choice-3-response = I will. But first I owe you a story shorter than the hangover after it, or you will think I am just stalling with style.

heretek-dialogue-fizzo-intro-choice-4 = I will take the whiskey
heretek-dialogue-fizzo-intro-choice-4-selected = Fine. Refusing that ending now would just be rude.
heretek-dialogue-fizzo-intro-choice-4-response = Good. A conversation should know when to step aside for good whiskey.
heretek-dialogue-fizzo-intro-complete-chat = Do not be shy. I always have room for a refill, and I only charge extra questions to people who actually want to talk.

heretek-dialogue-fizzo-encore-001 = You again. By the look in your eyes, either the day turned out worse than usual or the first glass was too honest.
heretek-dialogue-fizzo-encore-001b = She notices me coming back before I have even decided whether I returned for the whiskey or for that strange, almost comforting way she talks.
heretek-dialogue-fizzo-encore-002 = Another one?
heretek-dialogue-fizzo-encore-003 = The counter is not going anywhere. Come back when you want another swallow, a little silence, or just to hear someone stop pretending everything is fine.

heretek-dialogue-fizzo-encore-choice-1 = Yes, another one
heretek-dialogue-fizzo-encore-choice-1-selected = Yes. Pour me another.
heretek-dialogue-fizzo-encore-choice-1-response = Fine. But if you start arguing with a stool after this, I am taking the stool's side.

heretek-dialogue-fizzo-encore-choice-2 = No, I am good
heretek-dialogue-fizzo-encore-choice-2-selected = No, I have had enough for now.
heretek-dialogue-fizzo-encore-choice-2-response = Rare discipline. I respect it. So today you are still pretending you run your own life.

heretek-dialogue-fizzo-cutoff-chat = You have had enough. I pour whiskey here, not lift customers off the floor from under the counter.

heretek-dialogue-fizzo-base-001 = Back at the counter. What do you need?
heretek-dialogue-fizzo-base-choice-store = Open the shop
heretek-dialogue-fizzo-base-choice-whiskey = Another glass
heretek-dialogue-fizzo-base-choice-whiskey-selected = Another glass.
heretek-dialogue-fizzo-base-choice-rumors = Ask what is new on the station
heretek-dialogue-fizzo-base-choice-leave = Nothing, I should go

heretek-dialogue-fizzo-rumors-001 = News does not arrive at a bar counter. It crawls in, asks for a glass of water, and pretends nobody recognized it.
heretek-dialogue-fizzo-rumors-002 = What do you want to ask about?
heretek-dialogue-fizzo-rumors-choice-1 = The missing courier
heretek-dialogue-fizzo-rumors-choice-1-selected = People say a courier vanished near the cargo airlocks. Have you heard anything?
heretek-dialogue-fizzo-rumors-choice-1-response = I heard that he did not leave alone, and that he left an unfinished coffee on the table. For someone who always drank it to the bottom, that is nearly a cry for help. If you decide to look, start in the cargo corridor and do not trust the first person who says they saw nothing.
heretek-dialogue-fizzo-rumors-choice-1-followup = Ask again about the courier
heretek-dialogue-fizzo-rumors-choice-1-followup-selected = Anything new about that missing courier?
heretek-dialogue-fizzo-rumors-choice-1-followup-response = Nothing I would call proof. Someone scrubbed the cargo camera archive too cleanly, though. That is not absence of news; that is news wearing gloves. Look for whoever had access to the recorder before you chase shadows through the airlocks.
heretek-dialogue-fizzo-rumors-choice-2 = The quietest place
heretek-dialogue-fizzo-rumors-choice-2-selected = Where can someone find a little quiet on this station?
heretek-dialogue-fizzo-rumors-choice-2-response = Quiet? Nowhere. But by the old observation window, the noise is at least honest: only fans, stars, and your own thoughts. Sometimes that is enough.
heretek-dialogue-fizzo-rumors-choice-3 = Never mind, I should go
heretek-dialogue-fizzo-rumors-003 = If you hear something you do not want to carry alone, come back. I will have a stool and exactly as much silence as you need.
heretek-dialogue-ui-continue = >>>
