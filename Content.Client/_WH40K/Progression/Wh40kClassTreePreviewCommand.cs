using System.Collections.Generic;
using System.Linq;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.ClassProgression;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Progression;

/// <summary>
/// Local entry point for reviewing prototype-authored class skill trees without a server account.
/// </summary>
public sealed class Wh40kClassTreePreviewCommand : IConsoleCommand
{
    public string Command => "wh40k-preview-class-tree";
    public string Description => "Opens a prototype-backed class-tree preview for the requested class.";
    public string Help => $"Usage: {Command} <class-id|list>. Available class IDs are printed by '{Command} list'.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        var classes = prototypes.EnumeratePrototypes<Wh40kCharacterClassPrototype>()
            .OrderBy(characterClass => characterClass.Order)
            .ToArray();

        if (args.Length == 1 && string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            shell.WriteLine($"Class-tree preview IDs: {string.Join(", ", classes.Select(characterClass => characterClass.ID))}");
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteError(Help);
            return;
        }

        var requestedClassId = args.Length == 0 ? "soldier" : args[0];
        var selectedClass = classes.FirstOrDefault(characterClass =>
            string.Equals(characterClass.ID, requestedClassId, StringComparison.OrdinalIgnoreCase));
        if (selectedClass == null)
        {
            shell.WriteError($"Unknown class '{requestedClassId}'. Use '{Command} list' to see available IDs.");
            return;
        }

        var specializations = prototypes.EnumeratePrototypes<Wh40kClassSpecializationPrototype>()
            .Where(specialization => specialization.Class.Id == selectedClass.ID)
            .OrderBy(specialization => specialization.Order)
            .ToArray();
        var skills = prototypes.EnumeratePrototypes<Wh40kClassSkillPrototype>().ToArray();
        var doctrineSnapshots = specializations
            .Select(specialization => new Wh40kClassSpecializationSnapshot(
                specialization.ID,
                skills
                    .Where(skill => skill.Specialization.Id == specialization.ID)
                    .OrderBy(skill => skill.Order)
                    .Select(skill => new Wh40kClassSkillNodeSnapshot(
                        skill.ID,
                        skill.Order == 1
                            ? Wh40kClassSkillNodeState.Available
                            : Wh40kClassSkillNodeState.MissingPrerequisite))
                .ToList()))
            .ToList();

        var initialSkillId = doctrineSnapshots
            .SelectMany(snapshot => snapshot.Skills)
            .Select(skill => skill.SkillId)
            .FirstOrDefault();
        if (initialSkillId == null)
        {
            shell.WriteError($"Class '{selectedClass.ID}' has no class-tree skills to preview.");
            return;
        }

        var tree = new Wh40kClassTreeSnapshot(
            1,
            Wh40kClassProgressionConstants.TreeVersion,
            selectedClass.ID,
            1,
            0,
            7_500,
            7_500,
            0,
            0,
            new List<string>(),
            doctrineSnapshots);
        var window = new Wh40kClassTreeWindow();
        window.UpdateSnapshot(
            new Wh40kClassUiSnapshot(tree, new List<string>(), new List<Wh40kClassBonusUiSnapshot>()),
            Wh40kClassUiOperationStatus.None,
            initialSkillId);
        window.OpenCentered();
        window.FocusSpecializationOnOpen(null);
        shell.WriteLine($"Opened class-tree preview for '{selectedClass.ID}'.");
    }
}
