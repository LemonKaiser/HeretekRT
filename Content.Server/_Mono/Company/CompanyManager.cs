using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Mono.CCVar;
using Content.Shared._Mono.Company;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Mono.Company;

public sealed partial class CompanyManager
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ILogManager _log = default!;

    private ISawmill _sawmill = default!;

    private readonly Dictionary<ProtoId<CompanyPrototype>, HashSet<CompanyMemberRecord>> _companies = new();
    private readonly Dictionary<ProtoId<CompanyPrototype>, HashSet<FactionJoinRequestRecord>> _joinRequests = new();
    private Task _loading = Task.CompletedTask;
    public Task Ready => _loading;
    public event Action<ProtoId<CompanyPrototype>, NetUserId>? FactionChanged;

    public void Initialize()
    {
        _sawmill = _log.GetSawmill("company.manager");

        _net.RegisterNetMessage<MsgCompanyWhitelist>();
        _net.Connected += OnConnected;

        _loading = LoadCompaniesData();
    }

    private async Task LoadCompaniesData()
    {
        var sw = new Stopwatch();
        sw.Start();

        var members = await _db.GetAllCompanyMembers();
        var requests = await _db.GetAllFactionJoinRequests();
        _profiles = await _db.GetFactionProfilesAsync();
        _announcements = await _db.GetFactionAnnouncementsAsync();
        foreach (var relation in await _db.GetFactionRelationsAsync())
            _relations[(relation.Source, relation.Target)] = relation;
        _relationCooldowns = await _db.GetFactionRelationCooldownsAsync();
        foreach (var proto in _proto.EnumeratePrototypes<CompanyPrototype>())
        {
            _companies.Add(proto.ID, new());
            _companies[proto.ID].UnionWith(members.Where(m => m.Company == proto.ID).ToHashSet());
            _joinRequests.Add(proto.ID, new());
            _joinRequests[proto.ID].UnionWith(requests.Where(r => r.Company == proto.ID).ToHashSet());
        }

        _sawmill.Info($"All company members data loaded in {sw.Elapsed.TotalSeconds:.2}s");
    }

    private async void OnConnected(object? sender, NetChannelArgs e)
    {
        await Ready;
        if (e.Channel.IsConnected)
            SendCompanyWhitelist(e.Channel);
    }

    private async Task<HashSet<CompanyMemberRecord>> QueryCompanyMembers(ProtoId<CompanyPrototype> company)
    {
        return (await _db.GetCompanyMembers(company)).ToHashSet();
    }

    public HashSet<CompanyMemberRecord> GetCompanyMembers(ProtoId<CompanyPrototype> company)
    {
        if (!_companies.TryGetValue(company, out var members))
            return new();
        return members;
    }

    public HashSet<CompanyMemberRecord> GetAllCompanyMembers()
    {
        return _companies.Values.SelectMany(v => v).ToHashSet();
    }

    public HashSet<FactionJoinRequestRecord> GetFactionJoinRequests(ProtoId<CompanyPrototype> company)
    {
        return _joinRequests.TryGetValue(company, out var requests) ? requests : new();
    }

    public CompanyMemberRecord? GetCompanyMember(ProtoId<CompanyPrototype> company, NetUserId player)
    {
        if (!_companies.TryGetValue(company, out var members))
            return null;

        return members.FirstOrNull(m => m.PlayerUserId == player);
    }

    public void AddMember(NetUserId player, ProtoId<CompanyPrototype> company, FactionRole role = FactionRole.Member)
    {
        _ = AddMemberAsync(player, company, role);
    }

    public async Task<bool> AddMemberAsync(NetUserId player, ProtoId<CompanyPrototype> company, FactionRole role = FactionRole.Member)
    {
        await Ready;
        if (!_companies.ContainsKey(company) || IsMember(player, company))
            return false;

        await _db.AddCompanyMember(player, company);
        await _db.SetCompanyRole(company, player, role);

        var member = await _db.GetCompanyMember(company, player);
        if (member != null)
        {
            _companies[company].Add(member.Value);
            await RemoveJoinRequest(player, company);
        }

        if (_player.TryGetSessionById(player, out var session))
            SendCompanyWhitelist(session.Channel);

        FactionChanged?.Invoke(company, player);
        return member != null;
    }

    public bool IsAllowed(ICommonSession session, ProtoId<CompanyPrototype> company)
    {
        if (_proto.TryIndex(company, out var managed) && managed.JoinRequired)
            return IsMember(session.UserId, company);

        if (!_config.GetCVar(MonoCVars.CompanyWhitelist))
            return true;

        if (!_proto.TryIndex(company, out var companyPrototype) ||
            !companyPrototype.Whitelisted)
        {
            return true;
        }

        return IsMember(session.UserId, company);
    }

    public bool IsOwner(ICommonSession session, ProtoId<CompanyPrototype> company)
    {
        if (!_companies.TryGetValue(company, out var members))
            return false;

        var member = members.FirstOrNull(m => m.PlayerUserId == session.UserId);

        return member != null && member.Value.Role == FactionRole.Leader;
    }

    public FactionRole? GetRole(NetUserId player, ProtoId<CompanyPrototype> company)
    {
        return GetCompanyMember(company, player)?.Role;
    }

    public bool CanManage(ICommonSession session, ProtoId<CompanyPrototype> company, bool leaderOnly = false)
    {
        if (IsOwner(session, company))
            return true;

        return !leaderOnly && GetRole(session.UserId, company) == FactionRole.Officer;
    }

    public async Task<bool> RequestJoin(NetUserId player, ProtoId<CompanyPrototype> company)
    {
        await Ready;
        if (!_companies.ContainsKey(company) || IsMember(player, company))
            return false;

        var added = await _db.AddFactionJoinRequest(player, company);
        if (added)
        {
            var request = await _db.GetFactionJoinRequest(company, player);
            if (request != null)
                _joinRequests[company].Add(request.Value);
        }

        if (added)
            FactionChanged?.Invoke(company, player);
        return added;
    }

    public async Task<bool> RemoveJoinRequest(NetUserId player, ProtoId<CompanyPrototype> company)
    {
        var removed = await _db.RemoveFactionJoinRequest(player, company);
        if (removed && _joinRequests.TryGetValue(company, out var requests))
            requests.RemoveWhere(r => r.PlayerUserId == player);
        if (removed)
            FactionChanged?.Invoke(company, player);
        return removed;
    }

    public async Task<bool> AcceptJoinRequest(ICommonSession actor, ProtoId<CompanyPrototype> company, NetUserId target)
    {
        if (!CanManage(actor, company) || !_joinRequests.TryGetValue(company, out var requests) ||
            !requests.Any(r => r.PlayerUserId == target))
            return false;

        return await AddMemberAsync(target, company);
    }

    public async Task<bool> RejectJoinRequest(ICommonSession actor, ProtoId<CompanyPrototype> company, NetUserId target)
    {
        if (!CanManage(actor, company))
            return false;
        return await RemoveJoinRequest(target, company);
    }

    public async Task<bool> SetRole(ICommonSession actor, ProtoId<CompanyPrototype> company, NetUserId target, FactionRole role)
    {
        if (!CanManage(actor, company, leaderOnly: true) || target == actor.UserId)
            return false;

        if (GetCompanyMember(company, target) is not { } member || member.Role == FactionRole.Leader ||
            role is not (FactionRole.Member or FactionRole.Officer))
            return false;

        return await SetMemberRoleAsync(company, target, role);
    }

    public bool IsMember(NetUserId player, ProtoId<CompanyPrototype> company)
    {
        var member = GetCompanyMember(company, player);
        return member != null;
    }

    public async Task RemoveMember(NetUserId player, ProtoId<CompanyPrototype> company)
    {
        if (!_companies.ContainsKey(company))
            return;
        await _db.RemoveCompanyMember(player, company);
        _companies[company].RemoveWhere(w => w.PlayerUserId == player);

        if (_player.TryGetSessionById(player, out var session))
            SendCompanyWhitelist(session.Channel);
        FactionChanged?.Invoke(company, player);
    }

    public HashSet<ProtoId<CompanyPrototype>> GetPlayerCompanies(NetUserId player)
    {
        var res = new HashSet<ProtoId<CompanyPrototype>>();

        foreach (var (key, members) in _companies)
        {
            var member = members.FirstOrNull(m => m.PlayerUserId == player);
            if (member != null)
                res.Add(key);
        }

        return res;
    }

    public void SendCompanyWhitelist(INetChannel player)
    {
        var msg = new MsgCompanyWhitelist
        {
            Whitelist = GetPlayerCompanies(player.UserId)
        };

        _net.ServerSendMessage(msg, player);
    }
}
