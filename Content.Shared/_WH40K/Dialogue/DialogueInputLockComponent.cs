using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Dialogue;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedDialogueInputLockSystem))]
public sealed partial class DialogueInputLockComponent : Component
{
}
