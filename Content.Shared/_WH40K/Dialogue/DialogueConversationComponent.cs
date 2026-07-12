using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Dialogue;

/// <summary>
/// Replicated while an entity has active dialogue partners. Besides player feedback, this gives
/// other systems an authoritative way to distinguish an idle NPC from one serving several players.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DialogueConversationComponent : Component
{
    [DataField, AutoNetworkedField]
    public int ActiveSessions;

    [DataField, AutoNetworkedField]
    public bool HasSharedWorldSession;
}
