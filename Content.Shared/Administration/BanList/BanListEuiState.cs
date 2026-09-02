using Content.Shared.Eui;
using Robust.Shared.Serialization;
using Content.Shared._WH40K.Administration.Mute;

namespace Content.Shared.Administration.BanList;

[Serializable, NetSerializable]
public sealed class BanListEuiState : EuiStateBase
{
    public BanListEuiState(
        string banListPlayerName,
        List<SharedServerBan> bans,
        List<SharedServerRoleBan> roleBans,
        List<WH40KSharedMute> mutes,
        int muteHistoryOffset,
        bool hasNextMuteHistoryPage)
    {
        BanListPlayerName = banListPlayerName;
        Bans = bans;
        RoleBans = roleBans;
        Mutes = mutes;
        MuteHistoryOffset = muteHistoryOffset;
        HasNextMuteHistoryPage = hasNextMuteHistoryPage;
    }

    public string BanListPlayerName { get; }
    public List<SharedServerBan> Bans { get; }
    public List<SharedServerRoleBan> RoleBans { get; }
    public List<WH40KSharedMute> Mutes { get; }
    public int MuteHistoryOffset { get; }
    public bool HasNextMuteHistoryPage { get; }
}
