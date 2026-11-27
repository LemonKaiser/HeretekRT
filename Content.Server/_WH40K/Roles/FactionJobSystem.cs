using Content.Server.GameTicking.Events;
using Content.Server._Mono.Company;
using Content.Server.Preferences.Managers;
using Content.Server.Spawners.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Roles;

public sealed partial class FactionJobSystem : EntitySystem
{
    internal const string HereticJob = "WH40KHeretic";
    private const string ChaosCompany = "ChaosWarband";

    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CompanyManager _factions = default!;
    [Dependency] private CompanySystem _companies = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private StationJobsSystem _jobs = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IsJobAllowedEvent>(OnIsJobAllowed);
        SubscribeLocalEvent<GetDisallowedJobsEvent>(OnGetDisallowedJobs);
        SubscribeLocalEvent<StationJobsGetCandidatesEvent>(OnGetCandidates);
    }

    private string? GetCompany(NetUserId player)
    {
        var company = _preferences.TryGetCachedPreferences(player, out var preferences)
            ? (preferences.SelectedCharacter as HumanoidCharacterProfile)?.Company
            : null;
        return _factions.GetCharacterFaction(player, company);
    }

    internal bool TryGetHereticArrival(NetUserId player, out EntityCoordinates coordinates, out EntityUid station)
    {
        coordinates = EntityCoordinates.Invalid;
        station = EntityUid.Invalid;
        if (!_factions.IsMember(player, ChaosCompany))
            return false;

        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (points.MoveNext(out var uid, out var point, out var transform))
        {
            if (point.SpawnType != SpawnPointType.Job || point.Job != HereticJob ||
                _stations.GetOwningStation(uid, transform) is not { } owner ||
                !_jobs.TryGetJobSlot(owner, HereticJob, out var slots) || slots == 0)
                continue;

            coordinates = transform.Coordinates;
            station = owner;
            return true;
        }

        return false;
    }

    internal void AssignHereticArrival(NetUserId player, EntityUid mob, EntityUid station)
    {
        _jobs.TryAssignJob(station, HereticJob, player);
        _companies.SetFaction(mob, ChaosCompany);
    }

    private void OnIsJobAllowed(ref IsJobAllowedEvent ev)
    {
        if (_prototypes.TryIndex(ev.JobId, out var job) && !job.IsCompanyAllowed(GetCompany(ev.Player.UserId)))
            ev.Cancelled = true;
    }

    private void OnGetDisallowedJobs(ref GetDisallowedJobsEvent ev)
    {
        var company = GetCompany(ev.Player.UserId);
        foreach (var job in _prototypes.EnumeratePrototypes<JobPrototype>())
        {
            if (!job.IsCompanyAllowed(company))
                ev.Jobs.Add(job.ID);
        }
    }

    private void OnGetCandidates(ref StationJobsGetCandidatesEvent ev)
    {
        var company = GetCompany(ev.Player);
        ev.Jobs.RemoveAll(id => _prototypes.TryIndex(id, out var job) && !job.IsCompanyAllowed(company));
    }
}
