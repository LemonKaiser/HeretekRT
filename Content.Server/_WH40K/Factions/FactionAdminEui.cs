using System.Linq;
using System.Threading.Tasks;
using Content.Server._Mono.Company;
using Content.Server._WH40K.Localizations;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Factions;

public sealed class FactionAdminEui : BaseEui
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private CompanyManager _factions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IAdminLogManager _logs = default!;
    [Dependency] private ILogManager _logManager = default!;
    private string? _selected;
    private string _message = string.Empty;
    private bool _busy;
    private bool _closed;

    public FactionAdminEui() => IoCManager.InjectDependencies(this);

    private bool Authorized => !_closed && !IsShutDown && Player.Status != SessionStatus.Disconnected &&
                               _admins.GetAdminData(Player)?.HasFlag(AdminFlags.Admin) == true;

    public override void Opened()
    {
        _admins.OnPermsChanged += OnPermsChanged;
        _factions.FactionChanged += OnFactionChanged;
        _factions.FactionSocialChanged += OnSocialChanged;
        _ = ExecuteAsync(new FactionAdminRequest("", FactionAdminAction.Refresh));
    }

    public override void Closed()
    {
        _closed = true;
        _admins.OnPermsChanged -= OnPermsChanged;
        _factions.FactionChanged -= OnFactionChanged;
        _factions.FactionSocialChanged -= OnSocialChanged;
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !Authorized)
            Close();
    }

    private void OnFactionChanged(ProtoId<CompanyPrototype> faction, NetUserId target)
    {
        if (Authorized)
            StateDirty();
    }

    private void OnSocialChanged(string? faction)
    {
        if (faction == null && Authorized)
            StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        if (!Authorized || !_factions.Ready.IsCompletedSuccessfully)
            return new FactionAdminEuiState { Busy = _busy };
        var factions = _prototypes.EnumeratePrototypes<CompanyPrototype>().Where(f => f.ID != "None")
            .OrderBy(f => f.Name, StringComparer.Ordinal).Select(f =>
            {
                var members = _factions.GetCompanyMembers(f.ID);
                return new FactionAdminSummary(f.ID, f.Name, f.Disabled, f.JoinRequired, f.Whitelisted,
                    members.Count, members.Count(m => m.Role == FactionRole.Officer),
                    members.Where(m => m.Role == FactionRole.Leader).Select(m => m.LastSeenUserName).Order().ToArray(),
                    _factions.GetFactionJoinRequests(f.ID).Count);
            }).ToArray();
        if (!factions.Any(f => f.Id == _selected))
            _selected = factions.FirstOrDefault()?.Id;
        return new FactionAdminEuiState
        {
            Busy = _busy, Message = _message, SelectedFaction = _selected, Factions = factions,
            Profile = _selected == null ? new FactionProfile() : _factions.GetFactionProfile(_selected),
            Relations = _factions.GetFactionRelations(),
            Members = _selected == null ? [] : _factions.GetCompanyMembers(_selected)
                .OrderByDescending(m => m.Role).ThenBy(m => m.LastSeenUserName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new FactionAdminMember(m.PlayerUserId, m.LastSeenUserName, m.Role,
                    _players.TryGetSessionById(new NetUserId(m.PlayerUserId), out var session) && session.Status != SessionStatus.Disconnected)).ToArray(),
            Applications = _selected == null ? [] : _factions.GetFactionJoinRequests(_selected).OrderBy(r => r.CreatedAt)
                .Select(r => new FactionAdminApplication(r.PlayerUserId, r.LastSeenUserName, r.CreatedAt)).ToArray(),
        };
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (msg is CloseEuiMessage || _closed)
            return;
        if (!Authorized)
        {
            Close();
            return;
        }
        if (_busy)
            return;
        switch (msg)
        {
            case FactionAdminSelect select when !string.IsNullOrWhiteSpace(select.FactionId) && select.FactionId.Length <= 128 &&
                                               _prototypes.HasIndex<CompanyPrototype>(select.FactionId) && select.FactionId != "None":
                _selected = select.FactionId;
                _message = string.Empty;
                StateDirty();
                break;
            case FactionAdminRequest request when Enum.IsDefined(request.Action):
                _ = ExecuteAsync(request);
                break;
        }
    }

    private async Task ExecuteAsync(FactionAdminRequest request)
    {
        if (!Authorized || _busy)
            return;
        _busy = true;
        StateDirty();
        try
        {
            await _factions.Ready;
            if (request.Action == FactionAdminAction.Refresh)
            {
                _message = string.Empty;
                return;
            }
            if (request.FactionId != _selected || !_prototypes.TryIndex<CompanyPrototype>(request.FactionId, out var faction) || faction.ID == "None")
            {
                _message = Text("wh40k-faction-admin-invalid-faction");
                return;
            }
            var target = new NetUserId(request.TargetUserId);
            if (request.Action == FactionAdminAction.AddMember)
            {
                if (string.IsNullOrWhiteSpace(request.Target) || request.Target.Length > 128)
                    return;
                var located = await _locator.LookupIdByNameOrIdAsync(request.Target.Trim());
                if (located == null)
                {
                    _message = Text("wh40k-faction-admin-player-not-found");
                    return;
                }
                target = located.UserId;
                if (await _db.GetPlayerRecordByUserId(target) == null)
                {
                    _message = Text("wh40k-faction-admin-player-not-seen");
                    return;
                }
            }
            await _factions.FactionOperations.WaitAsync();
            try
            {
                if (!Authorized)
                    return;
                if (request.Action == FactionAdminAction.SetRelation)
                {
                    var status = await _factions.SetFactionRelationAsync(Player.UserId, faction.ID, request.Relation, Player.Name, admin: true);
                    _message = Text(status == Content.Shared._WH40K.Progression.Wh40kFactionUiOperationStatus.RelationSaved
                        ? "wh40k-faction-rel-saved" : status == Content.Shared._WH40K.Progression.Wh40kFactionUiOperationStatus.RelationConflict
                            ? "wh40k-faction-rel-conflict" : "wh40k-faction-rel-invalid");
                    if (status == Content.Shared._WH40K.Progression.Wh40kFactionUiOperationStatus.RelationSaved)
                        _logs.Add(LogType.AdminCommands, LogImpact.High,
                            $"Faction admin {Player.UserId}: relation {faction.ID} <-> {request.Relation!.Target}: {request.Relation.Score}: {request.Relation.Text}");
                    return;
                }
                _message = Text(await ActAsync(faction, target, request.Action));
            }
            finally
            {
                _factions.FactionOperations.Release();
            }
        }
        catch (Exception exception)
        {
            _logManager.GetSawmill("faction.admin").Error($"Faction admin action {request.Action} by {Player.UserId} failed: {exception}");
            if (Authorized)
                _message = Text("wh40k-faction-admin-failed");
        }
        finally
        {
            _busy = false;
            if (Authorized)
                StateDirty();
        }
    }

    private async Task<string> ActAsync(CompanyPrototype faction, NetUserId target, FactionAdminAction action)
    {
        var previous = _factions.GetCompanyMember(faction.ID, target);
        var name = previous?.LastSeenUserName ?? target.ToString();
        var previousLeaders = string.Join(", ", _factions.GetCompanyMembers(faction.ID)
            .Where(m => m.Role == FactionRole.Leader).Select(m => $"{m.LastSeenUserName} ({m.PlayerUserId})"));
        var changed = false;
        switch (action)
        {
            case FactionAdminAction.AddMember:
            case FactionAdminAction.AcceptApplication:
                if (previous != null)
                    return "wh40k-faction-admin-already-member";
                if (faction.JoinRequired && _factions.GetPlayerCompanies(target).Any(id => id != faction.ID && _prototypes.Index(id).JoinRequired))
                    return "wh40k-faction-admin-other-membership";
                if (action == FactionAdminAction.AcceptApplication && !_factions.GetFactionJoinRequests(faction.ID).Any(r => r.PlayerUserId == target))
                    return "wh40k-faction-admin-request-not-found";
                changed = await _factions.AddMemberAsync(target, faction.ID);
                break;
            case FactionAdminAction.RemoveMember when previous != null:
                await _factions.RemoveMember(target, faction.ID);
                changed = true;
                break;
            case FactionAdminAction.SetMember:
            case FactionAdminAction.SetOfficer:
            case FactionAdminAction.AssignLeader:
                var rank = action == FactionAdminAction.AssignLeader ? FactionRole.Leader
                    : action == FactionAdminAction.SetOfficer ? FactionRole.Officer : FactionRole.Member;
                changed = await _factions.SetMemberRoleAsync(faction.ID, target, rank);
                break;
            case FactionAdminAction.RejectApplication:
                changed = await _factions.RemoveJoinRequest(target, faction.ID);
                break;
        }
        if (!changed)
            return "wh40k-faction-admin-target-not-found";
        _logs.Add(LogType.AdminCommands, LogImpact.High,
            $"Faction admin {Player.Name} ({Player.UserId}): {action} for {name} ({target}) in {faction.ID}. Previous rank: {previous?.Role}; previous leaders: {previousLeaders}");
        return "wh40k-faction-admin-success";
    }

    private string Text(string key) => _entities.System<WH40KPlayerCultureManager>().GetPlayerString(Player, key);
}
