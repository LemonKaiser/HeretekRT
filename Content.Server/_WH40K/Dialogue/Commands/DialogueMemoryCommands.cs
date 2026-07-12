using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Database;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Dialogue.Commands;

/// <summary>
/// Read-only inspection of the semantic dialogue state stored for an account and dialogue-memory key.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class DialogueMemoryCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _playerLocator = default!;
    [Dependency] private IServerDbManager _db = default!;

    public string Command => "dialogue_memory";
    public string Description => "Shows persisted dialogue flags, counters, and completed dialogues for a player.";
    public string Help => "Usage: dialogue_memory <playerNameOrUserId> <persistentMemoryKey>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TryParseArguments(shell, args, out var playerIdentifier, out var memoryKey))
            return;

        var player = await _playerLocator.LookupIdByNameOrIdAsync(playerIdentifier);

        if (player == null)
        {
            shell.WriteError("Unable to find a player with that name or user ID.");
            return;
        }

        var memory = await _db.GetDialoguePersistentMemoryAsync(player.UserId, memoryKey);

        if (memory == null)
        {
            shell.WriteLine($"No persistent dialogue memory exists for {player.Username} ({player.UserId}) and key '{memoryKey}'.");
            return;
        }

        shell.WriteLine($"Dialogue memory for {player.Username} ({player.UserId}), key '{memoryKey}':");
        shell.WriteLine($"  Flags: {FormatValues(memory.Flags)}");
        shell.WriteLine($"  Counters: {FormatCounters(memory.Counters)}");
        shell.WriteLine($"  Completed: {FormatValues(memory.CompletedDialogues)}");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), "<playerNameOrUserId>")
            : CompletionResult.Empty;
    }

    internal static bool TryParseArguments(
        IConsoleShell shell,
        IReadOnlyList<string> args,
        out string playerIdentifier,
        out string memoryKey)
    {
        playerIdentifier = string.Empty;
        memoryKey = string.Empty;

        if (args.Count != 2)
        {
            shell.WriteError("Expected a player name/user ID and a persistent-memory key.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(args[1]) || args[1].Length > 128)
        {
            shell.WriteError("The persistent-memory key must contain 1 to 128 characters.");
            return false;
        }

        playerIdentifier = args[0];
        memoryKey = args[1];
        return true;
    }

    internal static string FormatValues(IEnumerable<string> values)
    {
        var formatted = values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return formatted.Length == 0 ? "<none>" : string.Join(", ", formatted);
    }

    internal static string FormatCounters(IReadOnlyDictionary<string, int> counters)
    {
        var formatted = counters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToArray();
        return formatted.Length == 0 ? "<none>" : string.Join(", ", formatted);
    }
}

/// <summary>
/// Clears one account's dialogue memory and invalidates matching active conversations.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class ResetDialogueMemoryCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _playerLocator = default!;
    [Dependency] private IEntitySystemManager _entitySystems = default!;

    public string Command => "dialogue_memory_reset";
    public string Description => "Clears persisted dialogue memory for a player and dialogue-memory key.";
    public string Help => "Usage: dialogue_memory_reset <playerNameOrUserId> <persistentMemoryKey>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!DialogueMemoryCommand.TryParseArguments(shell, args, out var playerIdentifier, out var memoryKey))
            return;

        var player = await _playerLocator.LookupIdByNameOrIdAsync(playerIdentifier);

        if (player == null)
        {
            shell.WriteError("Unable to find a player with that name or user ID.");
            return;
        }

        var dialogue = _entitySystems.GetEntitySystem<DialogueSystem>();
        await dialogue.ResetPersistentMemoryAsync(player.UserId, memoryKey);
        shell.WriteLine($"Cleared dialogue memory for {player.Username} ({player.UserId}) and key '{memoryKey}'.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), "<playerNameOrUserId>")
            : CompletionResult.Empty;
    }
}
