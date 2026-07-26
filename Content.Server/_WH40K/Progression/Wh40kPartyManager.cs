using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Owns active party snapshots and short-lived invitations. Persistent mutations remain in the DB manager.
/// </summary>
public sealed class Wh40kPartyManager
{
    public const int MaximumMembers = 5;
    public static readonly TimeSpan PartyLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromMinutes(1);

    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;

    private readonly Dictionary<Guid, Wh40kPartyRecord> _parties = new();
    private readonly Dictionary<NetUserId, Guid> _partyByUser = new();
    private readonly Dictionary<Guid, Wh40kPartyInvitation> _invitations = new();

    public event Action<Wh40kPartyRecord?>? PartyChanged;

    public async Task<Wh40kPartyRecord?> LoadAsync(
        NetUserId userId,
        CancellationToken cancel = default)
    {
        var party = await _db.GetWh40kPartyAsync(userId, cancel);
        if (party == null)
        {
            RemoveUserSnapshot(userId);
            return null;
        }

        Cache(party);
        return party;
    }

    public bool TryGetParty(NetUserId userId, out Wh40kPartyRecord party)
    {
        if (!_partyByUser.TryGetValue(userId, out var partyId) ||
            !_parties.TryGetValue(partyId, out party!) ||
            party.ExpiresAt <= DateTime.UtcNow)
        {
            party = default!;
            return false;
        }

        return true;
    }

    public bool AreInSameActiveParty(NetUserId first, NetUserId second)
    {
        return first != second &&
               TryGetParty(first, out var firstParty) &&
               TryGetParty(second, out var secondParty) &&
               firstParty.Id == secondParty.Id;
    }

    public string GetAttackingSideKey(NetUserId attacker)
    {
        return TryGetParty(attacker, out var party)
            ? $"party:{party.Id:N}"
            : $"account:{attacker.UserId:N}";
    }

    public async Task<Wh40kPartyMutationResult> CreateAsync(
        NetUserId leaderUserId,
        CancellationToken cancel = default)
    {
        var result = await _db.CreateWh40kPartyAsync(leaderUserId, cancel);
        if (result.Party != null)
            Cache(result.Party);
        return result;
    }

    public async Task<Wh40kPartyInvitationResult> InviteAsync(
        ICommonSession leader,
        string ckey,
        CancellationToken cancel = default)
    {
        ckey = ckey.Trim();
        if (ckey.Length == 0 ||
            !_players.TryGetSessionByUsername(ckey, out var target) ||
            target.Status == SessionStatus.Disconnected ||
            !target.Channel.AuthType.HasStaticUserId() ||
            target.UserId == leader.UserId)
        {
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.InvalidTarget);
        }

        var party = await LoadAsync(leader.UserId, cancel);
        if (party == null)
        {
            var created = await CreateAsync(leader.UserId, cancel);
            party = created.Party;
            if (party == null)
                return new Wh40kPartyInvitationResult(ToInvitationStatus(created.Status));
        }

        if (party.LeaderUserId != leader.UserId)
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.NotLeader, party);
        if (party.Members.Count >= MaximumMembers)
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.PartyFull, party);

        if (await LoadAsync(target.UserId, cancel) != null)
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.AlreadyInParty, party);
        if (!await _db.GetWh40kPartyInvitesAllowedAsync(target.UserId, cancel))
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.InvitesDisabled, party);

        RemoveInvitationsFor(target.UserId);
        var invitation = new Wh40kPartyInvitation(
            Guid.NewGuid(),
            party.Id,
            leader.UserId,
            target.UserId,
            DateTime.UtcNow + InvitationLifetime);
        _invitations.Add(invitation.Id, invitation);
        return new Wh40kPartyInvitationResult(
            Wh40kPartyInvitationStatus.Success,
            party,
            invitation);
    }

    public async Task<Wh40kPartyInvitationResult> AcceptInvitationAsync(
        ICommonSession target,
        Guid invitationId,
        CancellationToken cancel = default)
    {
        if (!_invitations.TryGetValue(invitationId, out var invitation) ||
            invitation.TargetUserId != target.UserId)
        {
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.InvitationNotFound);
        }

        _invitations.Remove(invitationId);
        if (invitation.ExpiresAt <= DateTime.UtcNow)
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.InvitationExpired);
        if (!await _db.GetWh40kPartyInvitesAllowedAsync(target.UserId, cancel))
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.InvitesDisabled);
        if (await LoadAsync(target.UserId, cancel) != null)
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.AlreadyInParty);

        var party = await LoadAsync(invitation.LeaderUserId, cancel);
        if (party == null ||
            party.Id != invitation.PartyId ||
            party.LeaderUserId != invitation.LeaderUserId)
        {
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.NotLeader);
        }

        if (party.Members.Count >= MaximumMembers)
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.PartyFull, party);

        var mutation = await _db.AddWh40kPartyMemberAsync(
            party.Id,
            invitation.LeaderUserId,
            target.UserId,
            party.Revision,
            cancel);
        if (mutation.Party != null)
            Cache(mutation.Party);

        return new Wh40kPartyInvitationResult(
            ToInvitationStatus(mutation.Status),
            mutation.Party);
    }

    public Wh40kPartyInvitationResult DeclineInvitation(NetUserId targetUserId, Guid invitationId)
    {
        if (!_invitations.TryGetValue(invitationId, out var invitation) ||
            invitation.TargetUserId != targetUserId)
        {
            return new Wh40kPartyInvitationResult(Wh40kPartyInvitationStatus.InvitationNotFound);
        }

        _invitations.Remove(invitationId);
        return new Wh40kPartyInvitationResult(
            invitation.ExpiresAt <= DateTime.UtcNow
                ? Wh40kPartyInvitationStatus.InvitationExpired
                : Wh40kPartyInvitationStatus.Success);
    }

    public async Task<Wh40kPartyMutationResult> LeaveAsync(
        NetUserId userId,
        CancellationToken cancel = default)
    {
        var party = await LoadAsync(userId, cancel);
        if (party == null)
            return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.NotInParty, null);

        var oldPartyId = party.Id;
        var result = await _db.LeaveWh40kPartyAsync(userId, party.Revision, cancel);
        if (party.LeaderUserId == userId || result.Party == null)
            RemovePartySnapshot(oldPartyId);
        if (result.Party != null)
            Cache(result.Party);
        return result;
    }

    public async Task<Wh40kPartyMutationResult> KickAsync(
        NetUserId leaderUserId,
        NetUserId memberUserId,
        CancellationToken cancel = default)
    {
        var party = await LoadAsync(leaderUserId, cancel);
        if (party == null)
            return new Wh40kPartyMutationResult(Wh40kPartyMutationStatus.NotInParty, null);

        var result = await _db.KickWh40kPartyMemberAsync(
            leaderUserId,
            memberUserId,
            party.Revision,
            cancel);
        if (result.Party != null)
            Cache(result.Party);
        else if (result.IsSuccess)
            RemoveUserSnapshot(memberUserId);
        return result;
    }

    public Task SetInvitesAllowedAsync(
        NetUserId userId,
        bool allowInvites,
        CancellationToken cancel = default)
    {
        if (!allowInvites)
            RemoveInvitationsFor(userId);
        return _db.SetWh40kPartyInvitesAllowedAsync(userId, allowInvites, cancel);
    }

    public void OnDisconnected(NetUserId userId)
    {
        foreach (var invitation in _invitations.Values
                     .Where(invitation =>
                         invitation.TargetUserId == userId ||
                         invitation.LeaderUserId == userId)
                     .ToArray())
        {
            _invitations.Remove(invitation.Id);
        }
    }

    public async Task CleanupExpiredAsync(CancellationToken cancel = default)
    {
        var now = DateTime.UtcNow;
        await _db.DeleteExpiredWh40kPartiesAsync(now, cancel);
        foreach (var party in _parties.Values.Where(party => party.ExpiresAt <= now).ToArray())
            RemovePartySnapshot(party.Id);
        foreach (var invitation in _invitations.Values.Where(invitation => invitation.ExpiresAt <= now).ToArray())
            _invitations.Remove(invitation.Id);
    }

    private void Cache(Wh40kPartyRecord party)
    {
        RemovePartySnapshot(party.Id);
        _parties[party.Id] = party;
        foreach (var member in party.Members)
        {
            if (_partyByUser.TryGetValue(member.UserId, out var previousParty) &&
                previousParty != party.Id)
            {
                RemovePartySnapshot(previousParty);
            }

            _partyByUser[member.UserId] = party.Id;
        }

        PartyChanged?.Invoke(party);
    }

    private void RemoveUserSnapshot(NetUserId userId)
    {
        if (!_partyByUser.Remove(userId, out var partyId))
            return;

        if (_parties.TryGetValue(partyId, out var party) &&
            party.Members.All(member => member.UserId != userId))
        {
            return;
        }

        RemovePartySnapshot(partyId);
    }

    private void RemovePartySnapshot(Guid partyId)
    {
        if (!_parties.Remove(partyId, out var previous))
            return;

        foreach (var member in previous.Members)
        {
            if (_partyByUser.TryGetValue(member.UserId, out var current) && current == partyId)
                _partyByUser.Remove(member.UserId);
        }
    }

    private void RemoveInvitationsFor(NetUserId targetUserId)
    {
        foreach (var invitation in _invitations.Values
                     .Where(invitation => invitation.TargetUserId == targetUserId)
                     .ToArray())
        {
            _invitations.Remove(invitation.Id);
        }
    }

    private static Wh40kPartyInvitationStatus ToInvitationStatus(Wh40kPartyMutationStatus status)
    {
        return status switch
        {
            Wh40kPartyMutationStatus.Success => Wh40kPartyInvitationStatus.Success,
            Wh40kPartyMutationStatus.AlreadyInParty => Wh40kPartyInvitationStatus.AlreadyInParty,
            Wh40kPartyMutationStatus.PartyFull => Wh40kPartyInvitationStatus.PartyFull,
            Wh40kPartyMutationStatus.NotLeader => Wh40kPartyInvitationStatus.NotLeader,
            Wh40kPartyMutationStatus.AccountNotFound => Wh40kPartyInvitationStatus.InvalidTarget,
            _ => Wh40kPartyInvitationStatus.InvitationNotFound,
        };
    }
}
