using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

public struct CompanyMemberRecord
{
    public Guid PlayerUserId;
    public string LastSeenUserName;
    public FactionRole Role;
    public ProtoId<CompanyPrototype> Company;

    // Compatibility for existing callers and legacy company commands.
    public bool Owner
    {
        readonly get => Role == FactionRole.Leader;
        set => Role = value ? FactionRole.Leader : FactionRole.Member;
    }
}
