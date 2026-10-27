using System.Linq;
using System.Threading.Tasks;
using Content.Shared._Mono.Company;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

public sealed partial class CompanyManager
{
    /// <summary>
    /// Mutations from both faction interfaces share one gate. Callers hold it for the complete operation.
    /// </summary>
    public System.Threading.SemaphoreSlim FactionOperations { get; } = new(1, 1);

    public async Task<bool> SetMemberRoleAsync(ProtoId<CompanyPrototype> company, NetUserId target, FactionRole role)
    {
        if (!Enum.IsDefined(role) || GetCompanyMember(company, target) is not { } member)
            return false;

        if (role == FactionRole.Leader)
        {
            if (!await _db.AssignFactionLeaderAsync(company, target))
                return false;
            var changed = _companies[company].Where(m => m.Role == FactionRole.Leader || m.PlayerUserId == target).ToArray();
            foreach (var previous in changed)
            {
                var updated = previous;
                updated.Role = updated.PlayerUserId == target ? FactionRole.Leader : FactionRole.Officer;
                _companies[company].Remove(previous);
                _companies[company].Add(updated);
            }
            foreach (var updated in changed)
                FactionChanged?.Invoke(company, new NetUserId(updated.PlayerUserId));
            return true;
        }

        await _db.SetCompanyRole(company, target, role);
        _companies[company].RemoveWhere(m => m.PlayerUserId == target);
        member.Role = role;
        _companies[company].Add(member);
        FactionChanged?.Invoke(company, target);
        return true;
    }
}
