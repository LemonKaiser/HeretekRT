using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.Players.JobWhitelist;
using Content.Shared.Administration;
using Content.Shared.Administration.Whitelist;
using Content.Shared.CCVar;
using Content.Shared.Eui;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server.Whitelist;

/// <summary>
/// Server-authoritative administrative panel for the manual server whitelist.
/// </summary>
public sealed partial class WhitelistPanelEui : BaseEui
{
    private const int MaximumPlayerIdentifierLength = 64;

    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IAdminActionGuard _actionGuard = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IServerDbManager _database = default!;
    [Dependency] private JobWhitelistManager _jobWhitelist = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerNetManager _network = default!;

    private readonly List<WhitelistPanelEntryState> _entries = new();
    private readonly ISawmill _sawmill;
    private bool _operationInProgress;

    public WhitelistPanelEui()
    {
        IoCManager.InjectDependencies(this);
        _sawmill = _log.GetSawmill("admin.whitelist_panel");
    }

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;
        _ = RefreshAsync();
    }

    public override void Closed()
    {
        base.Closed();
        _admins.OnPermsChanged -= OnPermsChanged;
    }

    public override EuiStateBase GetNewState()
    {
        var canManageMembers = CanManageMembers();
        return new WhitelistPanelEuiState(
            _configuration.GetCVar(CCVars.WhitelistEnabled),
            canManageMembers,
            CanToggleWhitelist(),
            CanKickNonWhitelisted(),
            _operationInProgress,
            canManageMembers ? new List<WhitelistPanelEntryState>(_entries) : new List<WhitelistPanelEntryState>());
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (msg is CloseEuiMessage)
            return;

        switch (msg)
        {
            case WhitelistPanelRefreshMessage:
                _ = RefreshAsync();
                break;
            case WhitelistPanelAddPlayerMessage add:
                _ = RunMutationAsync(() => AddPlayerAsync(add.PlayerIdentifier));
                break;
            case WhitelistPanelRemovePlayerMessage remove:
                _ = RunMutationAsync(() => RemovePlayerAsync(remove.UserId));
                break;
            case WhitelistPanelSetEnabledMessage setEnabled:
                _ = RunMutationAsync(() => SetWhitelistEnabledAsync(setEnabled.Enabled));
                break;
            case WhitelistPanelKickNonWhitelistedMessage:
                _ = RunMutationAsync(KickNonWhitelistedAsync);
                break;
        }
    }

    private bool CanManageMembers() => _admins.HasAdminFlag(Player, AdminFlags.Whitelist);

    private bool CanToggleWhitelist() => _admins.HasAdminFlag(Player, AdminFlags.Server);

    private bool CanKickNonWhitelisted() => _admins.HasAdminFlag(Player, AdminFlags.Ban);

    private async Task RunMutationAsync(Func<Task> operation)
    {
        if (_operationInProgress)
        {
            SendNotice("whitelist-panel-operation-in-progress", WhitelistPanelNoticeLevel.Info);
            return;
        }

        _operationInProgress = true;
        MarkStateDirty();
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Whitelist panel operation by {Player.Name} ({Player.UserId}) failed: {exception}");
            SendNotice("whitelist-panel-error-operation-failed", WhitelistPanelNoticeLevel.Error);
        }
        finally
        {
            _operationInProgress = false;
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (!CanManageMembers())
        {
            _entries.Clear();
            MarkStateDirty();
            return;
        }

        try
        {
            var players = await _database.GetWhitelistedPlayersAsync();
            if (IsShutDown)
                return;

            _entries.Clear();
            _entries.AddRange(players.Select(player =>
                new WhitelistPanelEntryState(player.UserId.UserId, player.UserName)));
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Failed to load whitelist panel data for {Player.Name} ({Player.UserId}): {exception}");
            SendNotice("whitelist-panel-error-load-failed", WhitelistPanelNoticeLevel.Error);
        }

        MarkStateDirty();
    }

    private async Task AddPlayerAsync(string playerIdentifier)
    {
        if (!CanManageMembers())
        {
            SendNotice("whitelist-panel-error-no-manage-permission", WhitelistPanelNoticeLevel.Error);
            return;
        }

        playerIdentifier = playerIdentifier.Trim();
        if (playerIdentifier.Length == 0 || playerIdentifier.Length > MaximumPlayerIdentifierLength)
        {
            SendNotice("whitelist-panel-error-invalid-player", WhitelistPanelNoticeLevel.Error);
            return;
        }

        var target = await _locator.LookupIdByNameOrIdAsync(playerIdentifier);
        if (target == null)
        {
            SendNotice("whitelist-panel-error-player-not-found", WhitelistPanelNoticeLevel.Error,
                ("player", playerIdentifier));
            return;
        }

        if (await _actionGuard.TryDenyProtectedTargetAsync(
                Player,
                target.UserId,
                Loc.GetString("admin-hierarchy-action-whitelist-add"),
                target.Username,
                SendError))
        {
            return;
        }

        if (!await _jobWhitelist.TryAddGlobalWhitelistAsync(target.UserId))
        {
            SendNotice("whitelist-panel-error-already-added", WhitelistPanelNoticeLevel.Info,
                ("player", target.Username));
            return;
        }

        _sawmill.Info($"{Player.Name} ({Player.UserId}) added {target.Username} ({target.UserId}) to the server whitelist.");
        SendNotice("whitelist-panel-player-added", WhitelistPanelNoticeLevel.Success, ("player", target.Username));
    }

    private async Task RemovePlayerAsync(Guid userId)
    {
        if (!CanManageMembers())
        {
            SendNotice("whitelist-panel-error-no-manage-permission", WhitelistPanelNoticeLevel.Error);
            return;
        }

        var targetId = new NetUserId(userId);
        var player = await _database.GetPlayerRecordByUserId(targetId);
        var targetName = player?.LastSeenUserName ?? userId.ToString();

        if (await _actionGuard.TryDenyProtectedTargetAsync(
                Player,
                targetId,
                Loc.GetString("admin-hierarchy-action-whitelist-remove"),
                targetName,
                SendError))
        {
            return;
        }

        if (!await _jobWhitelist.TryRemoveGlobalWhitelistAsync(targetId))
        {
            SendNotice("whitelist-panel-error-not-listed", WhitelistPanelNoticeLevel.Info,
                ("player", targetName));
            return;
        }

        _sawmill.Info($"{Player.Name} ({Player.UserId}) removed {targetName} ({targetId}) from the server whitelist.");
        SendNotice("whitelist-panel-player-removed", WhitelistPanelNoticeLevel.Success, ("player", targetName));
    }

    private Task SetWhitelistEnabledAsync(bool enabled)
    {
        if (!CanToggleWhitelist())
        {
            SendNotice("whitelist-panel-error-no-server-permission", WhitelistPanelNoticeLevel.Error);
            return Task.CompletedTask;
        }

        _configuration.SetCVar(CCVars.WhitelistEnabled, enabled);
        _sawmill.Info($"{Player.Name} ({Player.UserId}) {(enabled ? "enabled" : "disabled")} the server whitelist.");
        SendNotice(
            enabled ? "whitelist-panel-enabled" : "whitelist-panel-disabled",
            WhitelistPanelNoticeLevel.Success);
        return Task.CompletedTask;
    }

    private async Task KickNonWhitelistedAsync()
    {
        if (!CanKickNonWhitelisted())
        {
            SendNotice("whitelist-panel-error-no-kick-permission", WhitelistPanelNoticeLevel.Error);
            return;
        }

        if (!_configuration.GetCVar(CCVars.WhitelistEnabled))
        {
            SendNotice("whitelist-panel-error-whitelist-disabled", WhitelistPanelNoticeLevel.Error);
            return;
        }

        var kicked = 0;
        foreach (var session in _players.NetworkedSessions.ToArray())
        {
            // Preserve the behavior of the existing command: server staff are never removed by this bulk action.
            if (await _database.GetAdminDataForAsync(session.UserId) is not null ||
                await _database.GetWhitelistStatusAsync(session.UserId))
            {
                continue;
            }

            _network.DisconnectChannel(session.Channel, Loc.GetString("whitelist-not-whitelisted"));
            kicked++;
        }

        _sawmill.Info($"{Player.Name} ({Player.UserId}) kicked {kicked} non-whitelisted player(s).");
        SendNotice("whitelist-panel-kick-complete", WhitelistPanelNoticeLevel.Success, ("count", kicked));
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player)
            MarkStateDirty();
    }

    private void SendError(string message)
    {
        if (!IsShutDown)
            SendMessage(new WhitelistPanelNoticeMessage(message, WhitelistPanelNoticeLevel.Error));
    }

    private void SendNotice(string key, WhitelistPanelNoticeLevel level, params (string, object)[] args)
    {
        if (!IsShutDown)
            SendMessage(new WhitelistPanelNoticeMessage(Loc.GetString(key, args), level));
    }

    private void MarkStateDirty()
    {
        if (!IsShutDown)
            StateDirty();
    }
}
