using System;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared.Administration;
using Content.Shared.Preferences;
using Content.Shared._WH40K.CharacterCreation;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Administration.Commands;

/// <summary>
/// Displays the persisted introductory profile without requiring the player to be online.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class Wh40kProfileCommand : IConsoleCommand
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public string Command => "wh40kprofile";
    public string Description => "Показывает сохранённый профиль персонажа WH40K.";
    public string Help => "wh40kprofile <игрок или UserId>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
        if (located == null)
        {
            shell.WriteError($"Игрок '{args[0]}' не найден.");
            return;
        }

        PlayerPreferences? preferences;
        if (!_preferences.TryGetCachedPreferences(located.UserId, out preferences))
            preferences = await _db.GetPlayerPreferencesAsync(located.UserId, default);

        if (preferences == null ||
            !preferences.Characters.TryGetValue(preferences.SelectedCharacterIndex, out var profile) ||
            profile is not HumanoidCharacterProfile humanoid)
        {
            shell.WriteError($"У игрока '{located.Username}' нет выбранного гуманоидного профиля.");
            return;
        }

        var progress = await _db.GetWh40kPlayerProgressAsync(located.UserId);
        var build = humanoid.Wh40kBuild.Validated();
        Wh40kHomeworldPrototype? homeworld = null;
        Wh40kOriginPrototype? origin = null;
        Wh40kCharacterClassPrototype? characterClass = null;
        if (build.HomeworldId is { } homeworldId)
            _prototypes.TryIndex(homeworldId, out homeworld);
        if (build.OriginId is { } originId)
            _prototypes.TryIndex(originId, out origin);
        if (build.ClassId is { } classId)
            _prototypes.TryIndex(classId, out characterClass);

        shell.WriteLine($"WH40K-профиль: {located.Username} ({located.UserId})");
        shell.WriteLine($"Слот: {preferences.SelectedCharacterIndex}; персонаж: {humanoid.Name}; вид: {humanoid.Species}; возраст: {humanoid.Age}; пол: {humanoid.Sex}; гендер: {humanoid.Gender}.");
        shell.WriteLine(progress is null
            ? "Прогресс анкеты: отсутствует (старый профиль)."
            : $"Прогресс анкеты: акт {progress.Value.ActStage}; состояние {progress.Value.OnboardingStatus}; стартовый слот {progress.Value.OnboardingProfileSlot}.");
        shell.WriteLine($"Родной мир: {FormatSelection(build.HomeworldId, homeworld?.Name)}.");
        shell.WriteLine($"Происхождение: {FormatSelection(build.OriginId, origin?.Name)}.");
        shell.WriteLine($"Класс: {FormatSelection(build.ClassId, characterClass?.Name)}.");
        shell.WriteLine($"Портрет: {build.PortraitId ?? "не выбран"}.");
        shell.WriteLine($"Черты: {(humanoid.TraitPreferences.Count == 0 ? "не выбраны" : string.Join(", ", humanoid.TraitPreferences.OrderBy(id => id)))}.");

        shell.WriteLine("Характеристики (распределено / итог с модификаторами):");
        foreach (var characteristic in Enum.GetValues<Wh40kCharacteristic>())
        {
            var allocated = build.CharacteristicPoints.GetValueOrDefault(characteristic);
            var total = build.GetCharacteristicTotal(characteristic, homeworld, origin, characterClass);
            shell.WriteLine($"  {CharacteristicName(characteristic)}: {allocated} / {total}.");
        }

        if (!string.IsNullOrWhiteSpace(humanoid.FlavorText))
            shell.WriteLine($"Описание: {humanoid.FlavorText}");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            _players.Sessions.Select(player => player.Name).OrderBy(name => name),
            "<игрок или UserId>");
    }

    private static string FormatSelection(string? id, string? name)
    {
        return id == null ? "не выбрано" : name == null ? $"{id} (прототип не найден)" : $"{name} ({id})";
    }

    private static string CharacteristicName(Wh40kCharacteristic characteristic)
    {
        return characteristic switch
        {
            Wh40kCharacteristic.Melee => "Ближний бой",
            Wh40kCharacteristic.Ranged => "Дальний бой",
            Wh40kCharacteristic.Endurance => "Выносливость",
            Wh40kCharacteristic.Intelligence => "Интеллект",
            Wh40kCharacteristic.Agility => "Ловкость",
            _ => characteristic.ToString(),
        };
    }
}
