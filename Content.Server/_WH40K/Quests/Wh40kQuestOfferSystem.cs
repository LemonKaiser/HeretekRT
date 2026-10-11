using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WH40K.Quests;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Quests;

/// <summary>Bounded, deterministic weighted draws from reviewed templates. Browsing never spawns world entities.</summary>
public sealed partial class Wh40kQuestOfferSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private Wh40kQuestContentSystem _content = default!;
    [Dependency] private Wh40kQuestSessionSystem _sessions = default!;

    public Task<IReadOnlyList<Wh40kQuestOffer>> GenerateAsync(NetUserId user, Wh40kQuestPoolPrototype pool)
    {
        if (!_sessions.IsReady(user) || !_content.IsValidPool(pool.ID)) return Task.FromResult<IReadOnlyList<Wh40kQuestOffer>>([]);
        var now = DateTime.UtcNow;
        var window = new DateTime(now.Ticks / pool.RefreshInterval.Ticks * pool.RefreshInterval.Ticks, DateTimeKind.Utc);
        var candidates = pool.Entries.Where(entry => _content.IsValidQuest(entry.Quest) && _prototypes.Index(entry.Quest).Enabled)
            .Select(entry => (entry, terms: Wh40kQuestContractSnapshot.Freeze(_prototypes.Index(entry.Quest),
                Wh40kQuestOwnerKey.Account(user), new(window), 1, 0))).Take(128).ToArray();
        return _sessions.TrackWork(GenerateOwnedAsync(user, pool, candidates, window, now));
    }

    private async Task<IReadOnlyList<Wh40kQuestOffer>> GenerateOwnedAsync(NetUserId user, Wh40kQuestPoolPrototype pool,
        (Wh40kQuestPoolEntry Entry, Wh40kQuestContractSnapshot Terms)[] candidates, DateTime window, DateTime now)
    {
        var partyContent = candidates.Any(x => _prototypes.Index(x.Entry.Quest).OwnerScope == Wh40kQuestOwnerScope.Party);
        if (partyContent)
        {
            var party = await _db.GetWh40kPartyAsync(user);
            if (party == null || party.ExpiresAt <= now) return [];
            candidates = candidates.Select(x => (x.Entry, x.Terms with
            {
                Owner = Wh40kQuestOwnerKey.Party(party.Id), Recipients = [..party.Members.Select(m => m.UserId.UserId).OrderBy(x => x)],
            })).ToArray();
        }
        return await GenerateCoreAsync(user, pool, candidates, window, now);
    }

    private async Task<IReadOnlyList<Wh40kQuestOffer>> GenerateCoreAsync(NetUserId user, Wh40kQuestPoolPrototype pool,
        (Wh40kQuestPoolEntry Entry, Wh40kQuestContractSnapshot Terms)[] candidates, DateTime window, DateTime now)
    {
        var owner = candidates.FirstOrDefault().Terms?.Owner ?? Wh40kQuestOwnerKey.Account(user);
        var eligible = await _db.GetEligibleWh40kQuestDefinitionsAsync(candidates.Select(x => x.Terms).ToArray(), pool.ActiveLimit, now);
        var prior = await _db.GetWh40kQuestsAsync(owner, history: true, limit: pool.HistoryLimit);
        var all = await _db.GetWh40kQuestOffersAsync(owner, now);
        var definitions = candidates.Select(x => x.Terms.DefinitionId).ToHashSet();
        var selected = all.Where(x => definitions.Contains(x.DefinitionId) && eligible.Contains(x.DefinitionId)).DistinctBy(x => x.DefinitionId).Take(pool.OfferCount).ToList();
        var seedScope = $"{owner.Id:N}:{pool.ID}:{window.Ticks}:{prior.FirstOrDefault()?.Id}";
        var drawn = candidates.Where(x => eligible.Contains(x.Terms.DefinitionId) && selected.All(offer => offer.DefinitionId != x.Terms.DefinitionId))
            .Select(x => (x.Entry, x.Terms, Hash: SHA256.HashData(Encoding.UTF8.GetBytes(seedScope + ":" + x.Terms.DefinitionId))))
            .OrderBy(x => -Math.Log((BinaryPrimitives.ReadUInt32LittleEndian(x.Hash) + 1.0) / (uint.MaxValue + 2.0)) /
                (x.Entry.Weight / (prior.Any(row => row.DefinitionId == x.Terms.DefinitionId) ? 4.0 : 1.0)))
            .Take(Math.Max(0, pool.OfferCount - selected.Count));
        foreach (var draw in drawn)
        {
            var id = new Guid(draw.Hash.AsSpan(0, 16));
            var terms = draw.Terms with { Seed = BinaryPrimitives.ReadInt64LittleEndian(draw.Hash) };
            await _db.PutWh40kQuestOfferAsync(new(id, terms, window + pool.RefreshInterval, pool.ActiveLimit));
        }
        // Include only durable open rows; a concurrent acceptance cannot be presented as a new offer.
        all = await _db.GetWh40kQuestOffersAsync(owner, now);
        return all.Where(x => definitions.Contains(x.DefinitionId) && eligible.Contains(x.DefinitionId)).DistinctBy(x => x.DefinitionId).Take(pool.OfferCount).ToArray();
    }
}
