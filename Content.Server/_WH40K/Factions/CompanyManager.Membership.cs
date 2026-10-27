using System.Linq;
using System.Threading.Tasks;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

public sealed partial class CompanyManager
{
    public ProtoId<CompanyPrototype>? GetActiveFaction(NetUserId player, string? preferred = null)
    {
        var memberships = GetPlayerCompanies(player)
            .Where(id => _proto.TryIndex(id, out var faction) && !faction.Disabled && faction.ID != "None")
            .OrderBy(id => id.Id, StringComparer.Ordinal).ToArray();
        foreach (var id in memberships)
        {
            if (id.Id == preferred)
                return id;
        }
        if (memberships.Length > 0)
            return memberships[0];
        return null;
    }

    public string GetCharacterFaction(NetUserId player, string? preferred)
    {
        if (GetActiveFaction(player, preferred) is { } membership)
            return membership.Id;
        return preferred != null && _proto.TryIndex<CompanyPrototype>(preferred, out var faction) && !faction.JoinRequired
            ? preferred : "None";
    }

    /// <summary>Caller holds FactionOperations. Administrative removal uses RemoveMember instead.</summary>
    public async Task<bool> LeaveFactionAsync(NetUserId player, ProtoId<CompanyPrototype> faction)
    {
        if (!FactionPermissions.CanLeave(GetRole(player, faction)))
            return false;
        await RemoveMember(player, faction);
        return true;
    }
}
