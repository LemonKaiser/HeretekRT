using System.Threading.Tasks;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Content.Shared._WH40K.Progression;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

public sealed partial class CompanyManager
{
    private Dictionary<string, FactionProfile> _profiles = new();

    public FactionProfile GetFactionProfile(ProtoId<CompanyPrototype> faction) =>
        _profiles.GetValueOrDefault(faction.Id) ?? new FactionProfile();

    /// <summary>Caller holds FactionOperations for the complete operation.</summary>
    public async Task<Wh40kFactionUiOperationStatus> SaveFactionProfileAsync(
        NetUserId actor, ProtoId<CompanyPrototype> faction, FactionProfile? profile)
    {
        if (!FactionProfile.CanEdit(GetRole(actor, faction)) || !_proto.TryIndex(faction, out var prototype) ||
            prototype.Disabled || prototype.ID == "None")
            return Wh40kFactionUiOperationStatus.NotAuthorized;
        if (profile == null || !profile.TryValidate(out var normalized))
            return Wh40kFactionUiOperationStatus.InvalidProfile;
        if (GetFactionProfile(faction).Revision != normalized.Revision)
            return Wh40kFactionUiOperationStatus.ProfileConflict;

        var saved = new FactionProfile(normalized.Revision + 1, normalized.Admission, normalized.Sections,
            normalized.LeaderTitle, normalized.OfficerTitle, normalized.MemberTitle);
        await _db.SaveFactionProfileAsync(faction.Id, saved);
        _profiles[faction.Id] = saved;
        _sawmill.Info($"Faction profile {faction} updated by {actor}, revision {saved.Revision}.");
        FactionChanged?.Invoke(faction, actor);
        return Wh40kFactionUiOperationStatus.ProfileSaved;
    }
}
