using System;
using System.Collections.Generic;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration.Whitelist;

[Serializable, NetSerializable]
public sealed class WhitelistPanelEuiState : EuiStateBase
{
    public readonly bool WhitelistEnabled;
    public readonly bool CanManageMembers;
    public readonly bool CanToggleWhitelist;
    public readonly bool CanKickNonWhitelisted;
    public readonly bool OperationInProgress;
    public readonly List<WhitelistPanelEntryState> Entries;

    public WhitelistPanelEuiState(
        bool whitelistEnabled,
        bool canManageMembers,
        bool canToggleWhitelist,
        bool canKickNonWhitelisted,
        bool operationInProgress,
        List<WhitelistPanelEntryState> entries)
    {
        WhitelistEnabled = whitelistEnabled;
        CanManageMembers = canManageMembers;
        CanToggleWhitelist = canToggleWhitelist;
        CanKickNonWhitelisted = canKickNonWhitelisted;
        OperationInProgress = operationInProgress;
        Entries = entries;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelEntryState
{
    public readonly Guid UserId;
    public readonly string Ckey;

    public WhitelistPanelEntryState(Guid userId, string ckey)
    {
        UserId = userId;
        Ckey = ckey;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelRefreshMessage : EuiMessageBase;

[Serializable, NetSerializable]
public sealed class WhitelistPanelAddPlayerMessage : EuiMessageBase
{
    public readonly string PlayerIdentifier;

    public WhitelistPanelAddPlayerMessage(string playerIdentifier)
    {
        PlayerIdentifier = playerIdentifier;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelRemovePlayerMessage : EuiMessageBase
{
    public readonly Guid UserId;

    public WhitelistPanelRemovePlayerMessage(Guid userId)
    {
        UserId = userId;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelSetEnabledMessage : EuiMessageBase
{
    public readonly bool Enabled;

    public WhitelistPanelSetEnabledMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelKickNonWhitelistedMessage : EuiMessageBase;

[Serializable, NetSerializable]
public enum WhitelistPanelNoticeLevel : byte
{
    Info,
    Success,
    Error,
}

[Serializable, NetSerializable]
public sealed class WhitelistPanelNoticeMessage : EuiMessageBase
{
    public readonly string Message;
    public readonly WhitelistPanelNoticeLevel Level;

    public WhitelistPanelNoticeMessage(string message, WhitelistPanelNoticeLevel level)
    {
        Message = message;
        Level = level;
    }
}
