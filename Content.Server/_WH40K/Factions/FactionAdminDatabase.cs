using System.Linq;
using System.Threading.Tasks;
using Content.Shared._Mono.Company;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Prototypes;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<bool> AssignFactionLeaderAsync(ProtoId<CompanyPrototype> faction, Guid target);
}

public sealed partial class ServerDbManager
{
    public Task<bool> AssignFactionLeaderAsync(ProtoId<CompanyPrototype> faction, Guid target)
        => RunDbCommand(() => _db.AssignFactionLeaderAsync(faction, target));
}

public abstract partial class ServerDbBase
{
    public async Task<bool> AssignFactionLeaderAsync(ProtoId<CompanyPrototype> faction, Guid target)
    {
        await using var db = await GetDb();
        var members = await db.DbContext.CompanyMembers.AsTracking()
            .Where(member => member.CompanyId == faction.Id).ToListAsync();
        var nextLeader = members.FirstOrDefault(member => member.PlayerUserId == target);
        if (nextLeader == null)
            return false;

        foreach (var member in members.Where(member => member.Owner || member.Role == (byte) FactionRole.Leader))
        {
            member.Owner = false;
            member.Role = (byte) FactionRole.Officer;
        }
        nextLeader.Owner = true;
        nextLeader.Role = (byte) FactionRole.Leader;
        await db.DbContext.SaveChangesAsync();
        return true;
    }
}
