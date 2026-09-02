using System.Threading.Tasks;
using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Administration.BanList;
using Content.Shared._WH40K.Administration.Mute;
using Content.Shared.Eui;
using Robust.Shared.Network;

namespace Content.Server.Administration.BanList;

public sealed partial class BanListEui : BaseEui
{
    private const int MuteHistoryPageSize = 100;

    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IPlayerLocator _playerLocator = default!;
    [Dependency] private IServerDbManager _db = default!;

    public BanListEui()
    {
        IoCManager.InjectDependencies(this);
    }

    private Guid BanListPlayer { get; set; }
    private string BanListPlayerName { get; set; } = string.Empty;
    private List<SharedServerBan> Bans { get; } = new();
    private List<SharedServerRoleBan> RoleBans { get; } = new();
    private List<WH40KSharedMute> Mutes { get; } = new();
    private int MuteHistoryOffset { get; set; }
    private bool HasNextMuteHistoryPage { get; set; }

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

    public override EuiStateBase GetNewState()
    {
        return new BanListEuiState(
            BanListPlayerName,
            Bans,
            RoleBans,
            Mutes,
            MuteHistoryOffset,
            HasNextMuteHistoryPage);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (msg is BanListEuiMessages.SetMuteHistoryOffset request)
            _ = LoadMuteHistoryAsync(Math.Max(0, request.Offset));
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !_admins.HasAdminFlag(Player, AdminFlags.Ban))
        {
            Close();
        }
    }

    private async Task LoadBans(NetUserId userId)
    {
        foreach (var ban in await _db.GetServerBansAsync(null, userId, null, null))
        {
            SharedServerUnban? unban = null;
            if (ban.Unban is { } unbanDef)
            {
                var unbanningAdmin = unbanDef.UnbanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(unbanDef.UnbanningAdmin.Value))?.Username;
                unban = new SharedServerUnban(unbanningAdmin, ban.Unban.UnbanTime.UtcDateTime);
            }

            (string, int cidrMask)? ip = ("*Hidden*", 0);
            var hwid = "*Hidden*";

            if (_admins.HasAdminFlag(Player, AdminFlags.Pii))
            {
                ip = ban.Address is { } address
                    ? (address.address.ToString(), address.cidrMask)
                    : null;

                hwid = ban.HWId?.ToString();
            }

            Bans.Add(new SharedServerBan(
                ban.Id,
                ban.UserId,
                ip,
                hwid,
                ban.BanTime.UtcDateTime,
                ban.ExpirationTime?.UtcDateTime,
                ban.Reason,
                ban.BanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(ban.BanningAdmin.Value))?.Username,
                unban
            ));
        }
    }

    private async Task LoadRoleBans(NetUserId userId)
    {
        foreach (var ban in await _db.GetServerRoleBansAsync(null, userId, null, null))
        {
            SharedServerUnban? unban = null;
            if (ban.Unban is { } unbanDef)
            {
                var unbanningAdmin = unbanDef.UnbanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(unbanDef.UnbanningAdmin.Value))?.Username;
                unban = new SharedServerUnban(unbanningAdmin, ban.Unban.UnbanTime.UtcDateTime);
            }

            (string, int cidrMask)? ip = ("*Hidden*", 0);
            var hwid = "*Hidden*";

            if (_admins.HasAdminFlag(Player, AdminFlags.Pii))
            {
                ip = ban.Address is { } address
                    ? (address.address.ToString(), address.cidrMask)
                    : null;

                hwid = ban.HWId?.ToString();
            }
            RoleBans.Add(new SharedServerRoleBan(
                ban.Id,
                ban.UserId,
                ip,
                hwid,
                ban.BanTime.UtcDateTime,
                ban.ExpirationTime?.UtcDateTime,
                ban.Reason,
                ban.BanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(ban.BanningAdmin.Value))?.Username,
                unban,
                ban.Role
            ));
        }
    }

    private async Task LoadMutes(NetUserId userId, int offset)
    {
        var page = await _db.GetMuteHistoryAsync(userId, offset, MuteHistoryPageSize);
        var administratorIds = page.Entries
            .SelectMany(mute => new[] { mute.MutingAdmin, mute.Unmute?.UnmutingAdmin })
            .OfType<NetUserId>()
            .Distinct()
            .ToArray();
        var administratorNames = (await Task.WhenAll(administratorIds.Select(async id =>
                (Id: id, Name: (await _playerLocator.LookupIdAsync(id))?.Username))))
            .ToDictionary(pair => pair.Id, pair => pair.Name);

        Mutes.Clear();
        MuteHistoryOffset = offset;
        HasNextMuteHistoryPage = page.HasNextPage;
        foreach (var mute in page.Entries)
        {
            SharedServerUnban? unmute = null;
            if (mute.Unmute is { } unmuteDef)
            {
                var unmutingAdmin = unmuteDef.UnmutingAdmin is { } id
                    ? administratorNames.GetValueOrDefault(id)
                    : null;
                unmute = new SharedServerUnban(unmutingAdmin, unmuteDef.UnmuteTime.UtcDateTime);
            }

            Mutes.Add(new WH40KSharedMute(
                mute.Id ?? 0,
                mute.Type,
                mute.MuteTime.UtcDateTime,
                mute.ExpirationTime?.UtcDateTime,
                mute.Reason,
                mute.MutingAdmin is { } mutingAdmin
                    ? administratorNames.GetValueOrDefault(mutingAdmin)
                    : null,
                unmute));
        }
    }

    private async Task LoadFromDb()
    {
        Bans.Clear();
        RoleBans.Clear();
        Mutes.Clear();
        MuteHistoryOffset = 0;
        HasNextMuteHistoryPage = false;

        var userId = new NetUserId(BanListPlayer);
        BanListPlayerName = (await _playerLocator.LookupIdAsync(userId))?.Username ??
                            string.Empty;

        await LoadBans(userId);
        await LoadRoleBans(userId);
        await LoadMutes(userId, MuteHistoryOffset);

        StateDirty();
    }

    public async Task ChangeBanListPlayer(Guid banListPlayer)
    {
        BanListPlayer = banListPlayer;
        await LoadFromDb();
    }

    private async Task LoadMuteHistoryAsync(int offset)
    {
        if (BanListPlayer == Guid.Empty)
            return;

        await LoadMutes(new NetUserId(BanListPlayer), offset);
        StateDirty();
    }
}
