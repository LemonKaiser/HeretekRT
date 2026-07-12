using System.Collections.Generic;

namespace Content.Server.Database;

/// <summary>
/// Semantic dialogue state that is safe to keep between server restarts.
/// Transient values such as interaction cooldowns are intentionally excluded.
/// </summary>
public sealed class DialoguePersistentMemoryData
{
    public List<string> Flags { get; init; } = [];
    public Dictionary<string, int> Counters { get; init; } = new();
    public List<string> CompletedDialogues { get; init; } = [];
}
