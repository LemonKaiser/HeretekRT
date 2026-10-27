using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Shared._WH40K.Factions;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<Dictionary<string, FactionProfile>> GetFactionProfilesAsync();
    Task SaveFactionProfileAsync(string faction, FactionProfile profile);
}

public sealed partial class ServerDbManager
{
    public Task<Dictionary<string, FactionProfile>> GetFactionProfilesAsync()
        => RunDbCommand(() => _db.GetFactionProfilesAsync());

    public Task SaveFactionProfileAsync(string faction, FactionProfile profile)
        => RunDbCommand(() => _db.SaveFactionProfileAsync(faction, profile));
}

public abstract partial class ServerDbBase
{
    public async Task<Dictionary<string, FactionProfile>> GetFactionProfilesAsync()
    {
        await using var db = await GetDb();
        var rows = await db.DbContext.FactionProfiles.ToListAsync();
        return rows.ToDictionary(r => r.CompanyId, r => new FactionProfile(r.Revision, r.Admission,
            JsonSerializer.Deserialize<FactionProfileSection[]>(r.SectionsJson), r.LeaderTitle, r.OfficerTitle, r.MemberTitle));
    }

    public async Task SaveFactionProfileAsync(string faction, FactionProfile profile)
    {
        await using var db = await GetDb();
        var row = await db.DbContext.FactionProfiles.AsTracking().SingleOrDefaultAsync(r => r.CompanyId == faction);
        if (row == null)
        {
            row = new FactionProfileSettings { CompanyId = faction };
            db.DbContext.FactionProfiles.Add(row);
        }
        row.Revision = profile.Revision;
        row.Admission = profile.Admission;
        row.SectionsJson = JsonSerializer.Serialize(profile.Sections);
        row.LeaderTitle = profile.LeaderTitle;
        row.OfficerTitle = profile.OfficerTitle;
        row.MemberTitle = profile.MemberTitle;
        await db.DbContext.SaveChangesAsync();
    }
}
