using Content.Server.GameTicking.Events;
using Content.Server._Mono.Company;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Events;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Roles;

public sealed partial class FactionJobSystem : EntitySystem
{
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CompanyManager _factions = default!;

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
