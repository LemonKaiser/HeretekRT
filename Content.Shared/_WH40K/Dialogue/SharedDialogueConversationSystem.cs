using Content.Shared.Examine;

namespace Content.Shared._WH40K.Dialogue;

public sealed class SharedDialogueConversationSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DialogueConversationComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<DialogueConversationComponent> entity, ref ExaminedEvent args)
    {
        if (entity.Comp.ActiveSessions <= 0)
            return;

        args.PushMarkup(Loc.GetString(
            "heretek-dialogue-conversation-examine",
            ("count", entity.Comp.ActiveSessions)));
    }
}
