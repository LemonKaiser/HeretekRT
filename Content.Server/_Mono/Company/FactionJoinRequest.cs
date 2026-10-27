using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

public readonly record struct FactionJoinRequestRecord(
    Guid PlayerUserId,
    string LastSeenUserName,
    ProtoId<CompanyPrototype> Company,
    DateTime CreatedAt);
