using System;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Dialogue.Components;

[RegisterComponent]
public sealed partial class DialogueMemoryComponent : Component
{
    // Progress belongs to the account, not to a disposable body entity.
    public Dictionary<NetUserId, DialoguePlayerMemory> Players = new();
    public HashSet<NetUserId> PersistentPlayersLoaded = new();
}

public sealed class DialoguePlayerMemory
{
    public HashSet<string> Flags = new(StringComparer.Ordinal);
    public Dictionary<string, int> Counters = new(StringComparer.Ordinal);
    public Dictionary<string, TimeSpan> Cooldowns = new(StringComparer.Ordinal);
    public HashSet<string> CompletedDialogues = new(StringComparer.Ordinal);
}
