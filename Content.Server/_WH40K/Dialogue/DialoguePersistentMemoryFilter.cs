using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Database;
using Content.Server._WH40K.Dialogue.Components;

namespace Content.Server._WH40K.Dialogue;

/// <summary>
/// Converts in-memory dialogue progress to and from the subset that is allowed to survive a restart.
/// Keeping this logic separate also makes persistence policy directly testable.
/// </summary>
public static class DialoguePersistentMemoryFilter
{
    public static void Load(
        DialoguePlayerMemory memory,
        DialoguePersistentMemoryData data,
        DialogueInteractableComponent interactable)
    {
        if (interactable.PersistenceMode == DialogueMemoryPersistenceMode.All)
        {
            memory.Flags = new HashSet<string>(data.Flags, StringComparer.Ordinal);
            memory.Counters = new Dictionary<string, int>(data.Counters, StringComparer.Ordinal);
            memory.CompletedDialogues = new HashSet<string>(data.CompletedDialogues, StringComparer.Ordinal);
            return;
        }

        foreach (var flag in data.Flags.Where(interactable.PersistentFlags.Contains))
        {
            memory.Flags.Add(flag);
        }

        foreach (var (counter, value) in data.Counters)
        {
            if (interactable.PersistentCounters.Contains(counter))
                memory.Counters[counter] = value;
        }

        foreach (var dialogue in data.CompletedDialogues.Where(interactable.PersistentCompletedDialogues.Contains))
        {
            memory.CompletedDialogues.Add(dialogue);
        }
    }

    public static DialoguePersistentMemoryData Create(
        DialoguePlayerMemory memory,
        DialogueInteractableComponent interactable)
    {
        var flags = interactable.PersistenceMode == DialogueMemoryPersistenceMode.All
            ? memory.Flags
            : memory.Flags.Where(interactable.PersistentFlags.Contains);
        var counters = interactable.PersistenceMode == DialogueMemoryPersistenceMode.All
            ? memory.Counters
            : memory.Counters.Where(pair => interactable.PersistentCounters.Contains(pair.Key));
        var completedDialogues = interactable.PersistenceMode == DialogueMemoryPersistenceMode.All
            ? memory.CompletedDialogues
            : memory.CompletedDialogues.Where(interactable.PersistentCompletedDialogues.Contains);

        return new DialoguePersistentMemoryData
        {
            Flags = flags.OrderBy(flag => flag, StringComparer.Ordinal).ToList(),
            Counters = counters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            CompletedDialogues = completedDialogues.OrderBy(dialogue => dialogue, StringComparer.Ordinal).ToList()
        };
    }
}
