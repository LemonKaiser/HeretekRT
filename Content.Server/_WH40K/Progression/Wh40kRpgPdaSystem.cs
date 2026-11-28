using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared._WH40K.Progression;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Adapts the account RPG and party managers to private, actor-addressed GadOS BUI messages.
/// The PDA entity never owns or replicates an account progression snapshot.
/// </summary>
public sealed partial class Wh40kRpgPdaSystem : EntitySystem
{
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private Wh40kCharacterStatResolver _resolver = default!;
    [Dependency] private Wh40kPartyManager _parties = default!;
    [Dependency] private Wh40kProgressManager _progress = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kPlayerCartridgeComponent, CartridgeUiReadyEvent>(OnPlayerUiReady);
        SubscribeLocalEvent<Wh40kPlayerCartridgeComponent, CartridgeMessageEvent>(OnPlayerUiMessage);
        SubscribeLocalEvent<Wh40kPartyCartridgeComponent, CartridgeUiReadyEvent>(OnPartyUiReady);
        SubscribeLocalEvent<Wh40kPartyCartridgeComponent, CartridgeMessageEvent>(OnPartyUiMessage);

        _progress.ProgressChanged += OnProgressChanged;
    }

    public override void Shutdown()
    {
        _progress.ProgressChanged -= OnProgressChanged;
        base.Shutdown();
    }

    private async void OnPlayerUiReady(
        EntityUid uid,
        Wh40kPlayerCartridgeComponent component,
        CartridgeUiReadyEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session))
            return;

        await SendPlayerSnapshotAsync(
            args.Loader,
            args.Actor,
            session,
            Wh40kPlayerUiOperationStatus.None);
    }

    private async void OnPlayerUiMessage(
        EntityUid uid,
        Wh40kPlayerCartridgeComponent component,
        CartridgeMessageEvent args)
    {
        if (args is not Wh40kSpendCharacteristicsUiMessage message ||
            !_players.TryGetSessionByEntity(args.Actor, out var session))
        {
            return;
        }

        var result = await _progress.SpendCharacteristicsAsync(
            session,
            message.ExpectedRevision,
            message.Allocations);
        await SendPlayerSnapshotAsync(
            GetEntity(args.LoaderUid),
            args.Actor,
            session,
            ToUiStatus(result.Status));
    }

    private async void OnPartyUiReady(
        EntityUid uid,
        Wh40kPartyCartridgeComponent component,
        CartridgeUiReadyEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session))
            return;

        await SendPartySnapshotAsync(
            args.Loader,
            args.Actor,
            session,
            Wh40kPartyUiOperationStatus.None);
    }

    private async void OnPartyUiMessage(
        EntityUid uid,
        Wh40kPartyCartridgeComponent component,
        CartridgeMessageEvent args)
    {
        if (args is not Wh40kPartyUiMessage message ||
            !_players.TryGetSessionByEntity(args.Actor, out var session))
        {
            return;
        }

        var loader = GetEntity(args.LoaderUid);
        if (!session.Channel.AuthType.HasStaticUserId())
        {
            await SendPartySnapshotAsync(
                loader,
                args.Actor,
                session,
                Wh40kPartyUiOperationStatus.AccountUnavailable);
            return;
        }

        switch (message.Action)
        {
            case Wh40kPartyUiAction.Refresh:
                await SendPartySnapshotAsync(
                    loader,
                    args.Actor,
                    session,
                    Wh40kPartyUiOperationStatus.None);
                return;
            case Wh40kPartyUiAction.Invite:
                await InviteAsync(loader, args.Actor, session, message.Ckey);
                return;
            case Wh40kPartyUiAction.Leave:
                await LeaveAsync(loader, args.Actor, session);
                return;
            case Wh40kPartyUiAction.Kick:
                await KickAsync(loader, args.Actor, session, new NetUserId(message.TargetUserId));
                return;
            case Wh40kPartyUiAction.SetInvitesAllowed:
                await _parties.SetInvitesAllowedAsync(session.UserId, message.AllowInvites);
                await SendPartySnapshotAsync(
                    loader,
                    args.Actor,
                    session,
                    Wh40kPartyUiOperationStatus.Success);
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task InviteAsync(
        EntityUid loader,
        EntityUid actor,
        ICommonSession session,
        string ckey)
    {
        if (string.IsNullOrWhiteSpace(ckey) || ckey.Trim().Length > Wh40kRpgPdaConstants.MaximumCkeyLength)
        {
            await SendPartySnapshotAsync(
                loader,
                actor,
                session,
                Wh40kPartyUiOperationStatus.InvalidTarget);
            return;
        }

        var result = await _parties.InviteAsync(session, ckey);
        var status = ToUiStatus(result.Status);
        if (result.IsSuccess &&
            result.Invitation is { } invitation &&
            _players.TryGetSessionById(invitation.TargetUserId, out var target) &&
            target.Status != SessionStatus.Disconnected)
        {
            _eui.OpenEui(
                new Wh40kPartyInvitationEui(invitation, session.Name, this),
                target);
        }
        else if (result.Invitation is { } orphaned)
        {
            _parties.DeclineInvitation(orphaned.TargetUserId, orphaned.Id);
            status = Wh40kPartyUiOperationStatus.InvalidTarget;
        }

        await SendPartySnapshotAsync(loader, actor, session, status);
    }

    private async Task LeaveAsync(
        EntityUid loader,
        EntityUid actor,
        ICommonSession session)
    {
        var previous = _parties.TryGetParty(session.UserId, out var cached)
            ? cached
            : await _parties.LoadAsync(session.UserId);
        var result = await _parties.LeaveAsync(session.UserId);
        var status = ToUiStatus(result.Status);

        if (result.IsSuccess && previous != null)
        {
            foreach (var member in previous.Members)
            {
                await RefreshOpenPartyUiAsync(
                    member.UserId,
                    member.UserId == session.UserId ? status : Wh40kPartyUiOperationStatus.None);
            }

            return;
        }

        await SendPartySnapshotAsync(loader, actor, session, status);
    }

    private async Task KickAsync(
        EntityUid loader,
        EntityUid actor,
        ICommonSession session,
        NetUserId targetUserId)
    {
        var result = await _parties.KickAsync(session.UserId, targetUserId);
        var status = ToUiStatus(result.Status);
        await SendPartySnapshotAsync(loader, actor, session, status);
        if (result.IsSuccess)
        {
            await RefreshOpenPartyUiAsync(targetUserId, Wh40kPartyUiOperationStatus.None);
            if (result.Party != null)
                await RefreshPartyMembersAsync(result.Party, session.UserId);
        }
    }

    internal async Task ResolveInvitationAsync(
        ICommonSession target,
        NetUserId leaderUserId,
        Guid invitationId,
        bool accept)
    {
        var result = accept
            ? await _parties.AcceptInvitationAsync(target, invitationId)
            : _parties.DeclineInvitation(target.UserId, invitationId);
        var status = ToUiStatus(result.Status);

        await RefreshOpenPartyUiAsync(target.UserId, status);
        if (result.Party != null)
            await RefreshPartyMembersAsync(result.Party, target.UserId);
        else
            await RefreshOpenPartyUiAsync(leaderUserId, Wh40kPartyUiOperationStatus.None);
    }

    private async Task SendPlayerSnapshotAsync(
        EntityUid loader,
        EntityUid actor,
        ICommonSession session,
        Wh40kPlayerUiOperationStatus status)
    {
        var account = _progress.TryGetAccount(session.UserId, out var cached)
            ? cached
            : await _progress.LoadAsync(session.UserId);
        if (account == null)
        {
            SendPlayerMessage(
                loader,
                actor,
                new Wh40kPlayerSnapshotBuiMessage(
                    Wh40kPlayerUiOperationStatus.AccountUnavailable,
                    null));
            return;
        }

        var snapshot = CreatePlayerSnapshot(session, account);
        SendPlayerMessage(loader, actor, new Wh40kPlayerSnapshotBuiMessage(status, snapshot));
    }

    private void SendPlayerMessage(
        EntityUid loader,
        EntityUid actor,
        Wh40kPlayerSnapshotBuiMessage message)
    {
        _ui.ServerSendUiMessage(loader, PdaUiKey.Key, message, actor);
    }

    private async Task SendPartySnapshotAsync(
        EntityUid loader,
        EntityUid actor,
        ICommonSession session,
        Wh40kPartyUiOperationStatus status)
    {
        var snapshot = await CreatePartySnapshotAsync(session);
        _ui.ServerSendUiMessage(
            loader,
            PdaUiKey.Key,
            new Wh40kPartySnapshotBuiMessage(status, snapshot),
            actor);
    }

    private Wh40kPlayerProgressUiSnapshot CreatePlayerSnapshot(
        ICommonSession session,
        Wh40kAccountRpgRecord account)
    {
        var characterName = session.AttachedEntity is { Valid: true } mob && Exists(mob)
            ? Name(mob)
            : session.Name;
        return Wh40kRpgUiSnapshotFactory.CreatePlayer(
            characterName,
            account,
            _resolver.Resolve(account));
    }

    private async Task<Wh40kPartyUiSnapshot> CreatePartySnapshotAsync(ICommonSession session)
    {
        var invitesAllowed = session.Channel.AuthType.HasStaticUserId() &&
                             await _db.GetWh40kPartyInvitesAllowedAsync(session.UserId);
        var party = _parties.TryGetParty(session.UserId, out var cached)
            ? cached
            : await _parties.LoadAsync(session.UserId);
        var presentations = new Dictionary<NetUserId, Wh40kPartyMemberPresentation>();
        if (party != null)
        {
            foreach (var member in party.Members)
            {
                if (_players.TryGetSessionById(member.UserId, out var online) &&
                    online.Status != SessionStatus.Disconnected)
                {
                    presentations[member.UserId] = new Wh40kPartyMemberPresentation(online.Name, true);
                    continue;
                }

                var record = await _db.GetPlayerRecordByUserId(member.UserId);
                presentations[member.UserId] = new Wh40kPartyMemberPresentation(
                    record?.LastSeenUserName ?? member.UserId.ToString(),
                    false);
            }
        }

        return Wh40kRpgUiSnapshotFactory.CreateParty(
            session.UserId,
            party,
            invitesAllowed,
            presentations);
    }

    private void OnProgressChanged(NetUserId userId, Wh40kAccountRpgRecord account)
    {
        if (!_players.TryGetSessionById(userId, out var session) ||
            session.AttachedEntity is not { Valid: true } actor)
        {
            return;
        }

        var snapshot = CreatePlayerSnapshot(session, account);
        foreach (var (loader, key) in _ui.GetActorUis(actor))
        {
            if (!PdaUiKey.Key.Equals(key) ||
                !TryComp(loader, out CartridgeLoaderComponent? cartridgeLoader) ||
                cartridgeLoader.ActiveProgram is not { } program ||
                !HasComp<Wh40kPlayerCartridgeComponent>(program))
            {
                continue;
            }

            SendPlayerMessage(
                loader,
                actor,
                new Wh40kPlayerSnapshotBuiMessage(Wh40kPlayerUiOperationStatus.None, snapshot));
        }
    }

    private async Task RefreshOpenPartyUiAsync(
        NetUserId userId,
        Wh40kPartyUiOperationStatus status)
    {
        if (!_players.TryGetSessionById(userId, out var session) ||
            session.AttachedEntity is not { Valid: true } actor)
        {
            return;
        }

        var snapshot = await CreatePartySnapshotAsync(session);
        foreach (var (loader, key) in _ui.GetActorUis(actor))
        {
            if (!PdaUiKey.Key.Equals(key) ||
                !TryComp(loader, out CartridgeLoaderComponent? cartridgeLoader) ||
                cartridgeLoader.ActiveProgram is not { } program ||
                !HasComp<Wh40kPartyCartridgeComponent>(program))
            {
                continue;
            }

            _ui.ServerSendUiMessage(
                loader,
                PdaUiKey.Key,
                new Wh40kPartySnapshotBuiMessage(status, snapshot),
                actor);
        }
    }

    private async Task RefreshPartyMembersAsync(
        Wh40kPartyRecord party,
        NetUserId excludedUserId)
    {
        foreach (var member in party.Members)
        {
            if (member.UserId == excludedUserId)
                continue;

            await RefreshOpenPartyUiAsync(
                member.UserId,
                Wh40kPartyUiOperationStatus.None);
        }
    }

    private static Wh40kPlayerUiOperationStatus ToUiStatus(Wh40kCharacteristicSpendStatus status)
    {
        return status switch
        {
            Wh40kCharacteristicSpendStatus.Success => Wh40kPlayerUiOperationStatus.Success,
            Wh40kCharacteristicSpendStatus.AccountNotFound => Wh40kPlayerUiOperationStatus.AccountUnavailable,
            Wh40kCharacteristicSpendStatus.InvalidCharacteristic => Wh40kPlayerUiOperationStatus.InvalidCharacteristic,
            Wh40kCharacteristicSpendStatus.InvalidCount => Wh40kPlayerUiOperationStatus.InvalidCount,
            Wh40kCharacteristicSpendStatus.RevisionMismatch => Wh40kPlayerUiOperationStatus.RevisionMismatch,
            Wh40kCharacteristicSpendStatus.InsufficientDevelopmentPoints =>
                Wh40kPlayerUiOperationStatus.InsufficientDevelopmentPoints,
            _ => Wh40kPlayerUiOperationStatus.AccountUnavailable,
        };
    }

    private static Wh40kPartyUiOperationStatus ToUiStatus(Wh40kPartyMutationStatus status)
    {
        return status switch
        {
            Wh40kPartyMutationStatus.Success => Wh40kPartyUiOperationStatus.Success,
            Wh40kPartyMutationStatus.AccountNotFound => Wh40kPartyUiOperationStatus.AccountUnavailable,
            Wh40kPartyMutationStatus.AlreadyInParty => Wh40kPartyUiOperationStatus.AlreadyInParty,
            Wh40kPartyMutationStatus.NotInParty => Wh40kPartyUiOperationStatus.NotInParty,
            Wh40kPartyMutationStatus.PartyNotFound => Wh40kPartyUiOperationStatus.PartyNotFound,
            Wh40kPartyMutationStatus.PartyExpired => Wh40kPartyUiOperationStatus.PartyExpired,
            Wh40kPartyMutationStatus.NotLeader => Wh40kPartyUiOperationStatus.NotLeader,
            Wh40kPartyMutationStatus.PartyFull => Wh40kPartyUiOperationStatus.PartyFull,
            Wh40kPartyMutationStatus.RevisionMismatch => Wh40kPartyUiOperationStatus.RevisionMismatch,
            _ => Wh40kPartyUiOperationStatus.AccountUnavailable,
        };
    }

    private static Wh40kPartyUiOperationStatus ToUiStatus(Wh40kPartyInvitationStatus status)
    {
        return status switch
        {
            Wh40kPartyInvitationStatus.Success => Wh40kPartyUiOperationStatus.Success,
            Wh40kPartyInvitationStatus.InvalidTarget => Wh40kPartyUiOperationStatus.InvalidTarget,
            Wh40kPartyInvitationStatus.InvitesDisabled => Wh40kPartyUiOperationStatus.InvitesDisabled,
            Wh40kPartyInvitationStatus.AlreadyInParty => Wh40kPartyUiOperationStatus.AlreadyInParty,
            Wh40kPartyInvitationStatus.NotLeader => Wh40kPartyUiOperationStatus.NotLeader,
            Wh40kPartyInvitationStatus.PartyFull => Wh40kPartyUiOperationStatus.PartyFull,
            Wh40kPartyInvitationStatus.InvitationNotFound => Wh40kPartyUiOperationStatus.InvitationNotFound,
            Wh40kPartyInvitationStatus.InvitationExpired => Wh40kPartyUiOperationStatus.InvitationExpired,
            _ => Wh40kPartyUiOperationStatus.AccountUnavailable,
        };
    }
}
