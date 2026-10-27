using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Shared._WH40K.Factions;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<Dictionary<string, FactionAnnouncement[]>> GetFactionAnnouncementsAsync();
    Task SaveFactionAnnouncementsAsync(string faction, FactionAnnouncement[] posts);
    Task<FactionRelation[]> GetFactionRelationsAsync();
    Task<Dictionary<string, DateTime>> GetFactionRelationCooldownsAsync();
    Task SaveFactionRelationAsync(FactionRelation relation, string? changingFaction = null, DateTime? nextChangeAt = null);
}

public sealed partial class ServerDbManager
{
    public Task<Dictionary<string, FactionAnnouncement[]>> GetFactionAnnouncementsAsync() => RunDbCommand(() => _db.GetFactionAnnouncementsAsync());
    public Task SaveFactionAnnouncementsAsync(string faction, FactionAnnouncement[] posts) => RunDbCommand(() => _db.SaveFactionAnnouncementsAsync(faction, posts));
    public Task<FactionRelation[]> GetFactionRelationsAsync() => RunDbCommand(() => _db.GetFactionRelationsAsync());
    public Task<Dictionary<string, DateTime>> GetFactionRelationCooldownsAsync() => RunDbCommand(() => _db.GetFactionRelationCooldownsAsync());
    public Task SaveFactionRelationAsync(FactionRelation relation, string? changingFaction = null, DateTime? nextChangeAt = null) =>
        RunDbCommand(() => _db.SaveFactionRelationAsync(relation, changingFaction, nextChangeAt));
}

public abstract partial class ServerDbBase
{
    public async Task<Dictionary<string, FactionAnnouncement[]>> GetFactionAnnouncementsAsync()
    {
        await using var db = await GetDb();
        var rows = await db.DbContext.FactionBoards.ToListAsync();
        return rows.ToDictionary(r => r.CompanyId, r => JsonSerializer.Deserialize<FactionAnnouncement[]>(r.PostsJson) ?? []);
    }

    public async Task SaveFactionAnnouncementsAsync(string faction, FactionAnnouncement[] posts)
    {
        await using var db = await GetDb();
        var row = await db.DbContext.FactionBoards.AsTracking().SingleOrDefaultAsync(r => r.CompanyId == faction);
        if (row == null)
        {
            row = new FactionBoard { CompanyId = faction };
            db.DbContext.FactionBoards.Add(row);
        }
        row.PostsJson = JsonSerializer.Serialize(posts);
        await db.DbContext.SaveChangesAsync();
    }

    public async Task<FactionRelation[]> GetFactionRelationsAsync()
    {
        await using var db = await GetDb();
        var rows = await db.DbContext.FactionRelations.ToListAsync();
        return rows.Select(r => new FactionRelation(r.Source, r.Target, r.Score, r.Reason, r.Revision, r.Author, r.UpdatedAt)).ToArray();
    }

    public async Task<Dictionary<string, DateTime>> GetFactionRelationCooldownsAsync()
    {
        await using var db = await GetDb();
        return await db.DbContext.FactionRelationCooldowns.ToDictionaryAsync(r => r.CompanyId, r => r.NextChangeAt);
    }

    public async Task SaveFactionRelationAsync(FactionRelation relation, string? changingFaction = null, DateTime? nextChangeAt = null)
    {
        await using var db = await GetDb();
        var row = await db.DbContext.FactionRelations.AsTracking().SingleOrDefaultAsync(r => r.Source == relation.Source && r.Target == relation.Target);
        if (row == null)
        {
            row = new FactionRelationSettings { Source = relation.Source, Target = relation.Target };
            db.DbContext.FactionRelations.Add(row);
        }
        row.Score = relation.Score;
        row.Reason = relation.Reason;
        row.Revision = relation.Revision;
        row.Author = relation.Author;
        row.UpdatedAt = relation.UpdatedAt;
        if (changingFaction != null && nextChangeAt is { } availableAt)
        {
            var cooldown = await db.DbContext.FactionRelationCooldowns.AsTracking().SingleOrDefaultAsync(r => r.CompanyId == changingFaction);
            if (cooldown == null)
            {
                cooldown = new FactionRelationCooldown { CompanyId = changingFaction };
                db.DbContext.FactionRelationCooldowns.Add(cooldown);
            }
            cooldown.NextChangeAt = availableAt;
        }
        await db.DbContext.SaveChangesAsync();
    }
}
