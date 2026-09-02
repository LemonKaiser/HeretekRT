using System;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server._WH40K.Administration;
using Content.Shared.Eui;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Administration.Mute;

public sealed class WH40KMutePanelEui : BaseEui
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerLocator _playerLocator = default!;

    private string _playerName = string.Empty;
    private bool _muteRequestInFlight;
    private WH40KMuteSystem MuteSystem => _entities.System<WH40KMuteSystem>();

    public WH40KMutePanelEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override EuiStateBase GetNewState()
    {
        return new WH40KMutePanelEuiState(
            _playerName,
            WH40KStaffProtection.CanUseMuteTools(_admins.GetAdminData(Player)),
            _muteRequestInFlight);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        switch (msg)
        {
            case WH40KMutePanelEuiStateMsg.CreateMuteRequest create:
                _ = HandleCreateMuteAsync(create.Request);
                break;
            case WH40KMutePanelEuiStateMsg.GetPlayerInfoRequest request:
                _ = ChangePlayerAsync(request.PlayerUsername);
                break;
        }
    }

    public async Task ChangePlayerAsync(string playerNameOrId)
    {
        var located = await _playerLocator.LookupIdByNameOrIdAsync(playerNameOrId);
        ChangePlayer(located?.Username ?? string.Empty);
    }

    public void ChangePlayer(string playerName)
    {
        _playerName = playerName;
        StateDirty();
    }

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;
    }

    public override void Closed()
    {
        base.Closed();
        _admins.OnPermsChanged -= OnPermsChanged;
    }

    private async Task HandleCreateMuteAsync(WH40KCreateMuteRequest request)
    {
        if (_muteRequestInFlight)
            return;

        _muteRequestInFlight = true;
        StateDirty();
        var closeAfterRequest = false;
        try
        {
            if (!WH40KStaffProtection.CanUseMuteTools(_admins.GetAdminData(Player)))
                return;

            if (!WH40KMutePolicy.IsValidScopeMask(request.Type))
            {
                _chat.DispatchServerMessage(Player, Loc.GetString("wh40k-mute-panel-no-type"));
                return;
            }

            if (string.IsNullOrWhiteSpace(request.Target))
            {
                _chat.DispatchServerMessage(Player, Loc.GetString("wh40k-mute-panel-no-player"));
                return;
            }

            if (!WH40KMutePolicy.TryNormalizeReason(request.Reason, out _))
            {
                _chat.DispatchServerMessage(Player, Loc.GetString("wh40k-mute-panel-no-reason"));
                return;
            }

            TimeSpan? duration = request.DurationMinutes == 0
                ? null
                : TimeSpan.FromMinutes(request.DurationMinutes);
            if (!WH40KMutePolicy.IsValidTemporaryDuration(duration))
            {
                _chat.DispatchServerMessage(Player, Loc.GetString("wh40k-mute-command-invalid-duration"));
                return;
            }

            var located = await _playerLocator.LookupIdByNameOrIdAsync(request.Target);
            if (located == null)
            {
                _chat.DispatchServerMessage(Player, Loc.GetString("cmd-ban-player"));
                return;
            }

            if (await _adminActionGuard.TryDenyProtectedTargetAsync(
                    Player,
                    located.UserId,
                    Loc.GetString("wh40k-admin-hierarchy-action-mute"),
                    located.Username,
                    message => _chat.DispatchServerMessage(Player, message)))
            {
                return;
            }

            var result = await MuteSystem.ApplyMuteAsync(
                located.UserId,
                located.Username,
                request.Type,
                request.Reason,
                duration,
                Player.UserId,
                request.Erase);
            if (result != WH40KMuteApplyResult.Applied)
            {
                _chat.DispatchServerMessage(Player, MuteSystem.GetApplyFailureMessage(result));
                return;
            }

            _chat.DispatchServerMessage(
                Player,
                Loc.GetString("wh40k-mute-command-success", ("player", located.Username)));
            closeAfterRequest = true;
        }
        finally
        {
            _muteRequestInFlight = false;
            StateDirty();
        }

        if (closeAfterRequest)
            Close();
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player)
            StateDirty();
    }
}
