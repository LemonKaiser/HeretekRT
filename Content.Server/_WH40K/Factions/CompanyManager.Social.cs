using System.Linq;
using System.Threading.Tasks;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Content.Shared._WH40K.Progression;
using Robust.Shared.Network;

namespace Content.Server._Mono.Company;

public sealed partial class CompanyManager
{
    private Dictionary<string, FactionAnnouncement[]> _announcements = new();
    private readonly Dictionary<(string Source, string Target), FactionRelation> _relations = new();
    private Dictionary<string, DateTime> _relationCooldowns = new();
    public event Action<string?>? FactionSocialChanged;

    public FactionAnnouncement[] GetFactionAnnouncements(string faction) => _announcements.GetValueOrDefault(faction) ?? [];
    public FactionRelation[] GetFactionRelations() => _relations.Values
        .Where(r => ValidSocialFaction(r.Source) && ValidSocialFaction(r.Target)).OrderBy(r => r.Source).ThenBy(r => r.Target).ToArray();
    public FactionRelation GetFactionRelation(string source, string target)
    {
        var pair = FactionSocialRules.RelationPair(source, target);
        return _relations.GetValueOrDefault(pair) ?? new(pair.Source, pair.Target);
    }
    public int GetFactionRelationCooldown(string faction) =>
        FactionSocialRules.RelationCooldownSeconds(_relationCooldowns.GetValueOrDefault(faction), DateTime.UtcNow);
    private bool ValidSocialFaction(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        _proto.TryIndex<CompanyPrototype>(id, out var proto) && !proto.Disabled && id != "None";

    // Callers hold FactionOperations, shared with leadership transfers and member removal.
    public async Task<Wh40kFactionUiOperationStatus> PublishFactionAnnouncementAsync(NetUserId actor, string faction,
        FactionSocialRequest? request, string author)
    {
        if (!FactionSocialRules.CanPublish(GetRole(actor, faction)) || !ValidSocialFaction(faction))
            return Wh40kFactionUiOperationStatus.NotAuthorized;
        if (request == null || request.Id == Guid.Empty || !FactionSocialRules.ValidText(request.Title, FactionSocialRules.MaxTitleLength) ||
            !FactionSocialRules.ValidText(request.Text, FactionSocialRules.MaxAnnouncementLength, true))
            return Wh40kFactionUiOperationStatus.InvalidSocialText;
        var current = GetFactionAnnouncements(faction);
        if (current.Any(p => p.Id == request.Id))
            return Wh40kFactionUiOperationStatus.AnnouncementPublished;
        var post = new FactionAnnouncement(request.Id, author, request.Title.Trim(), request.Text.Trim(), DateTime.UtcNow);
        var next = current.Prepend(post).Take(FactionSocialRules.AnnouncementLimit).ToArray();
        await _db.SaveFactionAnnouncementsAsync(faction, next);
        _announcements[faction] = next;
        _sawmill.Info($"Faction announcement {post.Id} by {actor} ({author}) in {faction}: {post.Title}: {post.Text}");
        FactionSocialChanged?.Invoke(faction);
        return Wh40kFactionUiOperationStatus.AnnouncementPublished;
    }

    public async Task<Wh40kFactionUiOperationStatus> RemoveFactionAnnouncementAsync(NetUserId actor, string faction, Guid id)
    {
        if (!FactionSocialRules.CanPublish(GetRole(actor, faction)))
            return Wh40kFactionUiOperationStatus.NotAuthorized;
        var current = GetFactionAnnouncements(faction);
        var next = current.Where(p => p.Id != id).ToArray();
        if (next.Length == current.Length)
            return Wh40kFactionUiOperationStatus.AnnouncementRemoved;
        await _db.SaveFactionAnnouncementsAsync(faction, next);
        _announcements[faction] = next;
        _sawmill.Info($"Faction announcement {id} removed by {actor} in {faction}");
        FactionSocialChanged?.Invoke(faction);
        return Wh40kFactionUiOperationStatus.AnnouncementRemoved;
    }

    public async Task<Wh40kFactionUiOperationStatus> SetFactionRelationAsync(NetUserId actor, string source,
        FactionSocialRequest? request, string author, bool admin = false)
    {
        if (!admin && !FactionSocialRules.CanSetRelations(GetRole(actor, source)))
            return Wh40kFactionUiOperationStatus.NotAuthorized;
        if (request == null || !ValidSocialFaction(source) || !ValidSocialFaction(request.Target) || source == request.Target ||
            request.Revision < 0 || request.Revision == int.MaxValue ||
            (admin ? request.Score is < -100 or > 100 || request.Delta != 0
                : !FactionSocialRules.ValidRelationAdjustment(request.Delta) || request.Score != 0) ||
            !FactionSocialRules.ValidText(request.Text, FactionSocialRules.MaxReasonLength))
            return Wh40kFactionUiOperationStatus.InvalidRelation;
        var previous = GetFactionRelation(source, request.Target);
        if (!admin && GetFactionRelationCooldown(source) > 0)
            return Wh40kFactionUiOperationStatus.RelationCooldown;
        if (previous.Revision != request.Revision)
            return Wh40kFactionUiOperationStatus.RelationConflict;
        var score = admin ? request.Score : FactionSocialRules.AdjustRelation(previous.Score, request.Delta);
        if (!admin && score == previous.Score)
            return Wh40kFactionUiOperationStatus.InvalidRelation;
        var pair = FactionSocialRules.RelationPair(source, request.Target);
        var now = DateTime.UtcNow;
        var next = new FactionRelation(pair.Source, pair.Target, score, request.Text.Trim(), previous.Revision + 1, author, now);
        var availableAt = now + FactionSocialRules.RelationAdjustmentInterval;
        await _db.SaveFactionRelationAsync(next, admin ? null : source, admin ? null : availableAt);
        _relations[pair] = next;
        if (!admin)
            _relationCooldowns[source] = availableAt;
        _sawmill.Info($"Faction relation {source} <-> {request.Target} changed {previous.Score} -> {next.Score} by {actor} ({author}), admin={admin}: {next.Reason}");
        FactionSocialChanged?.Invoke(null);
        return Wh40kFactionUiOperationStatus.RelationSaved;
    }
}
