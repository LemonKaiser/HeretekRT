using System;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Administration.Mute;

[Serializable, NetSerializable]
public sealed class WH40KMutePanelEuiState(string playerName, bool canMute, bool requestInFlight) : EuiStateBase
{
    public string PlayerName { get; } = playerName;
    public bool CanMute { get; } = canMute;
    public bool RequestInFlight { get; } = requestInFlight;
}

public static class WH40KMutePanelEuiStateMsg
{
    [Serializable, NetSerializable]
    public sealed class CreateMuteRequest(WH40KCreateMuteRequest request) : EuiMessageBase
    {
        public WH40KCreateMuteRequest Request { get; } = request;
    }

    [Serializable, NetSerializable]
    public sealed class GetPlayerInfoRequest(string playerUsername) : EuiMessageBase
    {
        public string PlayerUsername { get; } = playerUsername;
    }
}

[Serializable, NetSerializable]
public sealed record WH40KCreateMuteRequest(
    string? Target,
    WH40KMuteType Type,
    uint DurationMinutes,
    string Reason,
    bool Erase);
