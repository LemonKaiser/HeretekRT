using System.Threading.Tasks;
using Content.Shared._WH40K.Progression;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Salvage;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Converts trusted gameplay events into the Stage 3 XP pipeline and cleans expired runtime state.
/// </summary>
public sealed class Wh40kProgressionEventSystem : EntitySystem
{
    private static readonly ProtoId<Wh40kExperienceSourcePrototype> SalvageSource =
        "Wh40kSalvageExpeditionCompleted";
    private static readonly ProtoId<Wh40kExperienceSourcePrototype> PvpSource =
        "Wh40kPvpPlayerKill";
    private static readonly ProtoId<Wh40kExperienceSourcePrototype> SupportSource =
        "Wh40kMedicalSupport";

    private const float MinimumUsefulHealing = 10f;
    private const float PartyCleanupInterval = 60f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Content.Server.GameTicking.GameTicker _gameTicker = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private Wh40kExperienceService _experience = default!;
    [Dependency] private Wh40kPartyManager _parties = default!;

    private readonly Wh40kSupportRewardLimiter _supportLimiter = new();
    private float _partyCleanupTimer;
    private bool _partyCleanupRunning;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kSalvageExpeditionCompletedEvent>(OnSalvageCompleted);
        SubscribeLocalEvent<Wh40kPlanetaryLandingCompletedEvent>(OnPlanetaryLanding);
        SubscribeLocalEvent<Wh40kUsefulHealingCompletedEvent>(OnUsefulHealing);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _partyCleanupTimer += frameTime;
        if (_partyCleanupTimer < PartyCleanupInterval || _partyCleanupRunning)
            return;

        _partyCleanupTimer = 0f;
        _partyCleanupRunning = true;
        _ = CleanupPartiesAsync();
    }

    private async Task CleanupPartiesAsync()
    {
        try
        {
            await _parties.CleanupExpiredAsync();
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to clean expired WH40K parties: {exception}");
        }
        finally
        {
            _partyCleanupRunning = false;
        }
    }

    private async void OnSalvageCompleted(Wh40kSalvageExpeditionCompletedEvent ev)
    {
        try
        {
            await _experience.AwardEventAsync(new Wh40kExperienceEventRequest(
                SalvageSource,
                $"salvage:{_gameTicker.RoundId}:{ev.Seed}",
                Maps: new HashSet<MapId> { ev.ExpeditionMap },
                DifficultyMultiplier: GetSalvageDifficultyMultiplier(ev.Difficulty),
                ContextJson: $"difficulty={ev.Difficulty};seed={ev.Seed}"));
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to award WH40K salvage expedition XP: {exception}");
        }
    }

    private async void OnPlanetaryLanding(Wh40kPlanetaryLandingCompletedEvent ev)
    {
        try
        {
            ProtoId<Wh40kExperienceSourcePrototype> source = ev.SourceId;
            await _experience.AwardEventAsync(new Wh40kExperienceEventRequest(
                source,
                $"exploration:{ev.BodyId}",
                Maps: new HashSet<MapId> { ev.SurfaceMap },
                IssuerEntity: ev.BodyId));
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to award WH40K planetary exploration XP: {exception}");
        }
    }

    private async void OnUsefulHealing(Wh40kUsefulHealingCompletedEvent ev)
    {
        if (ev.Helper == ev.Target ||
            ev.HealedDamage < MinimumUsefulHealing ||
            !_players.TryGetSessionByEntity(ev.Helper, out var helper) ||
            !_players.TryGetSessionByEntity(ev.Target, out var target) ||
            !_supportLimiter.TryRegister(helper.UserId, target.UserId, DateTime.UtcNow))
        {
            return;
        }

        try
        {
            await _experience.AwardEventAsync(new Wh40kExperienceEventRequest(
                SupportSource,
                $"support:{_gameTicker.RoundId}:{helper.UserId.UserId:N}:{target.UserId.UserId:N}:{_timing.CurTick}",
                helper.UserId,
                EventCoordinates: Transform(ev.Target).Coordinates,
                IssuerEntity: GetNetEntity(ev.Helper).ToString(),
                ContextJson: $"healedDamage={ev.HealedDamage:F2}"));
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to award WH40K medical support XP: {exception}");
        }
    }

    private async void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead ||
            ev.OldMobState == MobState.Dead ||
            ev.Origin is not { Valid: true } origin ||
            origin == ev.Target ||
            !_players.TryGetSessionByEntity(origin, out var attacker) ||
            !_players.TryGetSessionByEntity(ev.Target, out var victim))
        {
            return;
        }

        try
        {
            await _experience.AwardEventAsync(new Wh40kExperienceEventRequest(
                PvpSource,
                $"pvp:{_gameTicker.RoundId}:{victim.UserId.UserId:N}:{_timing.CurTick}",
                attacker.UserId,
                victim.UserId,
                Transform(ev.Target).Coordinates,
                IssuerEntity: GetNetEntity(origin).ToString()));
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to award WH40K PvP XP: {exception}");
        }
    }

    public static int GetSalvageDifficultyMultiplier(DifficultyRating difficulty)
    {
        return difficulty switch
        {
            DifficultyRating.Minimal or DifficultyRating.Minor =>
                Wh40kExperiencePolicy.FullMultiplier,
            DifficultyRating.Moderate => 1250,
            DifficultyRating.Hazardous => 1500,
            DifficultyRating.Extreme => 1750,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
        };
    }
}
