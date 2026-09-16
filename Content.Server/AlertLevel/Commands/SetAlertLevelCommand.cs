using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.Console;

namespace Content.Server.AlertLevel.Commands
{
    [UsedImplicitly]
    [AdminCommand(AdminFlags.Fun)]
    public sealed partial class SetAlertLevelCommand : LocalizedCommands
    {
        [Dependency] private IEntitySystemManager _entitySystems = default!;

        public override string Command => "setalertlevel";

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            var levelNames = new string[] {};
            var player = shell.Player;
            if (player?.AttachedEntity != null)
            {
                levelNames = GetDomainLevelNames(player.AttachedEntity.Value);
            }

            return args.Length switch
            {
                1 => CompletionResult.FromHintOptions(levelNames,
                    LocalizationManager.GetString("cmd-setalertlevel-hint-1")),
                2 => CompletionResult.FromHintOptions(CompletionHelper.Booleans,
                    LocalizationManager.GetString("cmd-setalertlevel-hint-2")),
                _ => CompletionResult.Empty,
            };
        }

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length < 1)
            {
                shell.WriteError(LocalizationManager.GetString("shell-wrong-arguments-number"));
                return;
            }

            var locked = false;
            if (args.Length > 1 && !bool.TryParse(args[1], out locked))
            {
                shell.WriteLine(LocalizationManager.GetString("shell-argument-must-be-boolean"));
                return;
            }

            var player = shell.Player;
            if (player?.AttachedEntity == null)
            {
                shell.WriteLine(LocalizationManager.GetString("shell-only-players-can-run-this-command"));
                return;
            }

            var alertLevels = _entitySystems.GetEntitySystem<AlertLevelSystem>();
            if (!alertLevels.TryGetDomainAlertLevel(player.AttachedEntity.Value, out _, out _))
            {
                shell.WriteLine(LocalizationManager.GetString("cmd-setalertlevel-invalid-grid"));
                return;
            }

            var level = args[0];
            var levelNames = GetDomainLevelNames(player.AttachedEntity.Value);
            if (!levelNames.Contains(level))
            {
                shell.WriteLine(LocalizationManager.GetString("cmd-setalertlevel-invalid-level"));
                return;
            }

            alertLevels.SetLevel(player.AttachedEntity.Value, level, true, true, true, locked);
        }

        private string[] GetDomainLevelNames(EntityUid entity)
        {
            var alerts = _entitySystems.GetEntitySystem<AlertLevelSystem>();
            if (!alerts.TryGetDomainAlertLevel(entity, out _, out var alertLevelComp)
                || alertLevelComp.AlertLevels == null)
                return Array.Empty<string>();

            return alertLevelComp.AlertLevels.Levels.Keys.ToArray();
        }
    }
}
