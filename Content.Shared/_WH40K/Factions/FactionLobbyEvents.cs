using Content.Shared._WH40K.Progression;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Factions;

[Serializable, NetSerializable]
public sealed class FactionLobbyRequestEvent(Wh40kFactionUiAction action, string factionId = "", Guid targetUserId = default,
    FactionProfile? profile = null, FactionBankPayout? payout = null, FactionSocialRequest? social = null) : EntityEventArgs
{
    public readonly FactionSocialRequest? Social = social;
    public readonly Wh40kFactionUiAction Action = action;
    public readonly string FactionId = factionId;
    public readonly Guid TargetUserId = targetUserId;
    public readonly FactionProfile? Profile = profile;
    public readonly FactionBankPayout? Payout = payout;
}

[Serializable, NetSerializable]
public sealed class FactionLobbyStateEvent(Wh40kFactionUiSnapshot snapshot, Wh40kFactionUiOperationStatus status) : EntityEventArgs
{
    public readonly Wh40kFactionUiSnapshot Snapshot = snapshot;
    public readonly Wh40kFactionUiOperationStatus Status = status;
}
