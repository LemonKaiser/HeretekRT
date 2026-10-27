using Content.Shared._Mono.Company;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Factions;

[Serializable, NetSerializable]
public sealed record FactionAdminSummary(string Id, string Name, bool Disabled, bool JoinRequired,
    bool Whitelisted, int Members, int Officers, string[] Leaders, int Applications);

[Serializable, NetSerializable]
public sealed record FactionAdminMember(Guid UserId, string Name, FactionRole Role, bool Online);

[Serializable, NetSerializable]
public sealed record FactionAdminApplication(Guid UserId, string Name, DateTime CreatedAt);

[Serializable, NetSerializable]
public sealed class FactionAdminEuiState : EuiStateBase
{
    public bool Busy { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? SelectedFaction { get; init; }
    public FactionAdminSummary[] Factions { get; init; } = [];
    public FactionAdminMember[] Members { get; init; } = [];
    public FactionAdminApplication[] Applications { get; init; } = [];
    public FactionProfile Profile { get; init; } = new();
    public FactionRelation[] Relations { get; init; } = [];
}

[Serializable, NetSerializable]
public enum FactionAdminAction : byte
{
    Refresh,
    AddMember,
    RemoveMember,
    SetMember,
    SetOfficer,
    AssignLeader,
    AcceptApplication,
    RejectApplication,
    SetRelation,
}

[Serializable, NetSerializable]
public sealed class FactionAdminSelect(string factionId) : EuiMessageBase
{
    public string FactionId { get; } = factionId;
}

[Serializable, NetSerializable]
public sealed class FactionAdminRequest(string factionId, FactionAdminAction action, Guid targetUserId = default,
    string target = "", FactionSocialRequest? relation = null) : EuiMessageBase
{
    public FactionSocialRequest? Relation { get; } = relation;
    public string FactionId { get; } = factionId;
    public FactionAdminAction Action { get; } = action;
    public Guid TargetUserId { get; } = targetUserId;
    public string Target { get; } = target;
}
