using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WH40K.Progression;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// The only trusted entry point for persistent and guest-session XP awards.
/// Stage 3 validates prototype sources, participation, anti-farm and active party splits here.
/// </summary>
public sealed class Wh40kExperienceService
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private Wh40kLevelRewardCatalog _levelRewards = default!;
    [Dependency] private Wh40kPartyManager _parties = default!;
    [Dependency] private Wh40kProgressManager _progress = default!;

    private readonly Wh40kPvpAntiFarmTracker _pvpAntiFarm = new();

    public async Task<Wh40kExperienceAwardResult> AwardAsync(
        NetUserId userId,
        Wh40kXpAwardRequest request,
        CancellationToken cancel = default)
    {
        if (_players.TryGetSessionById(userId, out var session) &&
            session.Channel.AuthType == LoginType.Guest)
        {
            return _progress.AwardTransient(userId, request);
        }

        request = request with { LevelRewards = _levelRewards.GetDefinitions() };
        var result = await _db.AwardWh40kExperienceAsync(userId, request, cancel);
        _progress.Cache(result.Account, result.IsAwarded && result.Ledger.AmountTenths > 0);
        if (result.IsAwarded && result.LevelsGained > 0)
            await _entities.System<Wh40kRewardDeliverySystem>().TryDeliverForUserAsync(userId, cancel);
        return result;
    }

    public async Task<Wh40kExperienceEventResult> AwardEventAsync(
        Wh40kExperienceEventRequest request,
        CancellationToken cancel = default)
    {
        var source = _prototypes.Index(request.Source);
        Validate(source, request);

        var amountTenths = Wh40kExperiencePolicy.ApplyMultiplier(
            source.AmountTenths,
            request.DifficultyMultiplier);
        Wh40kPvpAntiFarmResult? antiFarm = null;
        if (source.SourceType == Wh40kExperienceSourceType.Combat)
        {
            var attacker = request.PrimaryUserId!.Value;
            antiFarm = _pvpAntiFarm.Register(
                _parties.GetAttackingSideKey(attacker),
                request.TargetUserId!.Value,
                DateTime.UtcNow);
            amountTenths = Wh40kExperiencePolicy.ApplyMultiplier(amountTenths, antiFarm.Value.Multiplier);
        }

        var eligible = GetCandidates(request)
            .Where(session => IsParticipating(session, source, request))
            .DistinctBy(session => session.UserId)
            .ToArray();
        if (eligible.Length == 0)
            return new Wh40kExperienceEventResult(Array.Empty<Wh40kExperienceAwardResult>());

        var diagnosticContext = JsonSerializer.Serialize(new
        {
            source = source.ID,
            difficultyMultiplier = request.DifficultyMultiplier,
            antiFarmOccurrence = antiFarm?.Occurrence,
            antiFarmMultiplier = antiFarm?.Multiplier,
            adapterContext = request.ContextJson,
        });
        var roundId = _entities.System<GameTicker>().RoundId;
        var awards = new List<Wh40kExperienceAwardResult>();

        foreach (var group in eligible.GroupBy(GetRewardSideKey))
        {
            var split = Wh40kExperiencePolicy.Split(
                amountTenths,
                group.Select(session => session.UserId));
            foreach (var (userId, shareTenths) in split)
            {
                var award = await AwardAsync(
                    userId,
                    new Wh40kXpAwardRequest(
                        request.RewardId,
                        source.SourceType,
                        shareTenths,
                        roundId > 0 ? roundId : null,
                        request.IssuerEntity,
                        diagnosticContext),
                    cancel);
                awards.Add(award);
            }
        }

        return new Wh40kExperienceEventResult(awards);
    }

    private IEnumerable<ICommonSession> GetCandidates(Wh40kExperienceEventRequest request)
    {
        if (request.PrimaryUserId is not { } primaryUserId)
            return _players.Sessions.Where(session => session.Status == SessionStatus.InGame);

        if (_parties.TryGetParty(primaryUserId, out var party))
        {
            return party.Members
                .Select(member =>
                    _players.TryGetSessionById(member.UserId, out var session)
                        ? session
                        : null)
                .Where(session => session != null)
                .Select(session => session!)
                .Where(session => session.Status == SessionStatus.InGame);
        }

        return _players.TryGetSessionById(primaryUserId, out var primary)
            ? [primary]
            : Array.Empty<ICommonSession>();
    }

    private bool IsParticipating(
        ICommonSession session,
        Wh40kExperienceSourcePrototype source,
        Wh40kExperienceEventRequest request)
    {
        if (session.AttachedEntity is not { Valid: true } mob ||
            !_entities.TryGetComponent(mob, out TransformComponent? transform))
        {
            return false;
        }

        float? distance = null;
        if (source.Participation == Wh40kParticipationMode.Radius &&
            request.EventCoordinates is { } eventCoordinates &&
            eventCoordinates.TryDistance(_entities, transform.Coordinates, out var actualDistance))
        {
            distance = actualDistance;
        }

        return Wh40kExperiencePolicy.MatchesParticipation(
            source.Participation,
            distance,
            request.Grid is { } grid && transform.GridUid == grid,
            request.Maps?.Contains(transform.MapID) == true,
            source.Radius);
    }

    private string GetRewardSideKey(ICommonSession session)
    {
        return _parties.TryGetParty(session.UserId, out var party)
            ? $"party:{party.Id:N}"
            : $"account:{session.UserId.UserId:N}";
    }

    private static void Validate(
        Wh40kExperienceSourcePrototype source,
        Wh40kExperienceEventRequest request)
    {
        if (source.AmountTenths <= 0)
            throw new InvalidOperationException($"WH40K XP source {source.ID} has a non-positive amount.");
        if (!Enum.IsDefined(source.SourceType) || !Enum.IsDefined(source.Participation))
            throw new InvalidOperationException($"WH40K XP source {source.ID} has an invalid policy.");
        if (!Wh40kExperienceCurve.IsSupportedDifficultyMultiplier(request.DifficultyMultiplier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"WH40K XP source {source.ID} received an unsupported difficulty multiplier.");
        }
        if (source.Participation == Wh40kParticipationMode.Radius &&
            (source.Radius <= 0f || request.EventCoordinates == null))
        {
            throw new ArgumentException($"WH40K XP source {source.ID} requires event coordinates.", nameof(request));
        }
        if (source.Participation == Wh40kParticipationMode.Grid && request.Grid == null)
            throw new ArgumentException($"WH40K XP source {source.ID} requires a grid.", nameof(request));
        if (source.Participation is Wh40kParticipationMode.Sector or Wh40kParticipationMode.Expedition &&
            (request.Maps == null || request.Maps.Count == 0))
        {
            throw new ArgumentException($"WH40K XP source {source.ID} requires an allowed map set.", nameof(request));
        }
        if (source.SourceType == Wh40kExperienceSourceType.Combat &&
            (request.PrimaryUserId == null || request.TargetUserId == null))
        {
            throw new ArgumentException($"WH40K combat source {source.ID} requires attacker and victim.", nameof(request));
        }
    }
}
