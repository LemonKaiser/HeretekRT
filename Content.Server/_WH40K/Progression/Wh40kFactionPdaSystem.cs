using System.Linq;
using System.Threading.Tasks;
using Content.Server._Mono.Company;
using Content.Server._NF.Bank;
using Content.Server.Preferences.Managers;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Content.Shared._WH40K.Progression;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Progression;

public sealed partial class Wh40kFactionPdaSystem : EntitySystem
{
    [Dependency] private CompanyManager _factions = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private readonly HashSet<NetUserId> _lobbyViewers = [];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kFactionCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<Wh40kFactionCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeNetworkEvent<FactionLobbyRequestEvent>(OnLobbyRequest);
        _factions.FactionChanged += OnFactionChanged;
        _bank.FactionBankChanged += OnBankChanged;
        InitializeSocial();
    }

    public override void Shutdown()
    {
        _factions.FactionChanged -= OnFactionChanged;
        _bank.FactionBankChanged -= OnBankChanged;
        ShutdownSocial();
        _lobbyViewers.Clear();
        base.Shutdown();
    }

    private async void OnUiReady(EntityUid uid, Wh40kFactionCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session))
            return;
        await _factions.Ready;
        SendSnapshot(args.Loader, args.Actor, session, Wh40kFactionUiOperationStatus.None);
    }

    private async void OnUiMessage(EntityUid uid, Wh40kFactionCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not Wh40kFactionUiMessage message || !_players.TryGetSessionByEntity(args.Actor, out var session))
            return;
        var status = await ExecuteAsync(message, session);
        SendSnapshot(GetEntity(args.LoaderUid), args.Actor, session, status);
    }

    private async void OnLobbyRequest(FactionLobbyRequestEvent message, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        _lobbyViewers.Add(session.UserId);
        var status = await ExecuteAsync(new Wh40kFactionUiMessage(message.Action, message.FactionId, message.TargetUserId, message.Profile, message.Payout, message.Social), session);
        if (session.Channel.IsConnected)
            RaiseNetworkEvent(new FactionLobbyStateEvent(CreateSnapshot(session), status), session.Channel);
    }

    private async Task<Wh40kFactionUiOperationStatus> ExecuteAsync(Wh40kFactionUiMessage message, ICommonSession session)
    {
        await _factions.Ready;
        await _bank.FactionBankReady;
        await _factions.FactionOperations.WaitAsync();
        try
        {
            return await HandleActionAsync(message, session);
        }
        catch (Exception e)
        {
            Log.Error($"Faction operation {message.Action} failed for {session.UserId}: {e}");
            return Wh40kFactionUiOperationStatus.Failed;
        }
        finally
        {
            _factions.FactionOperations.Release();
        }
    }

    private async Task<Wh40kFactionUiOperationStatus> HandleActionAsync(Wh40kFactionUiMessage message, ICommonSession session)
    {
        if (message.Action == Wh40kFactionUiAction.Refresh)
            return Wh40kFactionUiOperationStatus.None;
        if (!session.Channel.AuthType.HasStaticUserId())
            return Wh40kFactionUiOperationStatus.AccountUnavailable;
        if (message.Action is Wh40kFactionUiAction.RequestJoin or Wh40kFactionUiAction.CancelRequest)
        {
            if (!_prototypes.TryIndex<CompanyPrototype>(message.FactionId, out var faction) || faction.Disabled || faction.ID == "None")
                return Wh40kFactionUiOperationStatus.InvalidFaction;
            if (message.Action == Wh40kFactionUiAction.CancelRequest)
                return await _factions.RemoveJoinRequest(session.UserId, faction.ID)
                    ? Wh40kFactionUiOperationStatus.RequestCancelled : Wh40kFactionUiOperationStatus.RequestNotFound;
            if (GetFaction(session) != null)
                return Wh40kFactionUiOperationStatus.AlreadyMember;
            return await _factions.RequestJoin(session.UserId, faction.ID)
                ? Wh40kFactionUiOperationStatus.RequestSubmitted : Wh40kFactionUiOperationStatus.AlreadyRequested;
        }

        if (GetFaction(session) is not { } current)
            return Wh40kFactionUiOperationStatus.NotAuthorized;
        var target = new NetUserId(message.TargetUserId);
        switch (message.Action)
        {
            case Wh40kFactionUiAction.SendMessage:
            case Wh40kFactionUiAction.SendDistress:
            case Wh40kFactionUiAction.PublishAnnouncement:
            case Wh40kFactionUiAction.RemoveAnnouncement:
            case Wh40kFactionUiAction.SetRelation:
                if (message.FactionId != current.Id)
                    return Wh40kFactionUiOperationStatus.NotAuthorized;
                return await HandleSocialAsync(message, session, current.Id);
            case Wh40kFactionUiAction.Payout:
                if (message.FactionId != current.Id)
                    return Wh40kFactionUiOperationStatus.NotAuthorized;
                return await _bank.PayFactionAsync(session.UserId, current.Id, target, message.Payout);
            case Wh40kFactionUiAction.SaveProfile:
                if (message.FactionId != current.Id)
                    return Wh40kFactionUiOperationStatus.NotAuthorized;
                return await _factions.SaveFactionProfileAsync(session.UserId, current, message.Profile);
            case Wh40kFactionUiAction.AcceptRequest:
                if (!_factions.CanManage(session, current))
                    return Wh40kFactionUiOperationStatus.NotAuthorized;
                if (_factions.GetPlayerCompanies(target).Any(id => _prototypes.Index(id).JoinRequired))
                    return Wh40kFactionUiOperationStatus.AlreadyMember;
                if (!await _factions.AcceptJoinRequest(session, current, target))
                    return Wh40kFactionUiOperationStatus.RequestNotFound;
                return Wh40kFactionUiOperationStatus.RequestAccepted;
            case Wh40kFactionUiAction.RejectRequest:
                return await _factions.RejectJoinRequest(session, current, target)
                    ? Wh40kFactionUiOperationStatus.RequestRejected : Wh40kFactionUiOperationStatus.NotAuthorized;
            case Wh40kFactionUiAction.RemoveMember:
                if (!FactionPermissions.CanRemove(_factions.GetRole(session.UserId, current),
                        _factions.GetRole(target, current), target == session.UserId))
                    return Wh40kFactionUiOperationStatus.NotAuthorized;
                await _factions.RemoveMember(target, current);
                return Wh40kFactionUiOperationStatus.MemberRemoved;
            case Wh40kFactionUiAction.PromoteOfficer:
            case Wh40kFactionUiAction.DemoteOfficer:
                var promote = message.Action == Wh40kFactionUiAction.PromoteOfficer;
                return await _factions.SetRole(session, current, target, promote ? FactionRole.Officer : FactionRole.Member)
                    ? promote ? Wh40kFactionUiOperationStatus.OfficerPromoted : Wh40kFactionUiOperationStatus.OfficerDemoted
                    : Wh40kFactionUiOperationStatus.NotAuthorized;
            case Wh40kFactionUiAction.Leave:
                if (_factions.GetRole(session.UserId, current) == FactionRole.Leader)
                    return Wh40kFactionUiOperationStatus.CannotManageLeader;
                return await _factions.LeaveFactionAsync(session.UserId, current)
                    ? Wh40kFactionUiOperationStatus.LeftFaction : Wh40kFactionUiOperationStatus.NotAuthorized;
            default:
                return Wh40kFactionUiOperationStatus.NotAuthorized;
        }
    }

    private void SendSnapshot(EntityUid loader, EntityUid actor, ICommonSession session, Wh40kFactionUiOperationStatus status)
    {
        if (session.Channel.IsConnected && Exists(loader) && Exists(actor))
            _ui.ServerSendUiMessage(loader, PdaUiKey.Key, new Wh40kFactionSnapshotBuiMessage(status, CreateSnapshot(session)), actor);
    }

    private Wh40kFactionUiSnapshot CreateSnapshot(ICommonSession session)
    {
        var available = _prototypes.EnumeratePrototypes<CompanyPrototype>()
            .Where(f => !f.Disabled && f.ID != "None")
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f =>
            {
                var roster = _factions.GetCompanyMembers(f.ID);
                var leader = roster.Where(m => m.Role == FactionRole.Leader)
                    .OrderBy(m => m.LastSeenUserName, StringComparer.OrdinalIgnoreCase)
                    .Select(m => m.LastSeenUserName).FirstOrDefault();
                return new Wh40kFactionOptionUiSnapshot(f.ID, f.Name, f.Description, f.Color.ToHex(),
                    _factions.GetRole(session.UserId, f.ID),
                    _factions.GetFactionJoinRequests(f.ID).Any(r => r.PlayerUserId == session.UserId),
                    leader, roster.Count, roster.Count(m => m.Role == FactionRole.Officer), _factions.GetFactionProfile(f.ID));
            })
            .ToList();
        var accountAvailable = session.Channel.AuthType.HasStaticUserId();
        if (GetFaction(session) is not { } factionId || !_prototypes.TryIndex(factionId, out var faction))
            return new Wh40kFactionUiSnapshot(null, null, null, null, null, available, [], [], accountAvailable,
                relations: _factions.GetFactionRelations());
        var role = _factions.GetRole(session.UserId, faction.ID);
        var members = _factions.GetCompanyMembers(faction.ID)
            .OrderByDescending(m => m.Role).ThenBy(m => m.LastSeenUserName, StringComparer.OrdinalIgnoreCase)
            .Select(m =>
            {
                var online = _players.TryGetSessionById(new NetUserId(m.PlayerUserId), out var other) && other.Status != SessionStatus.Disconnected;
                return new Wh40kFactionMemberUiSnapshot(m.PlayerUserId, online ? other!.Name : m.LastSeenUserName,
                    m.Role, m.PlayerUserId == session.UserId, online);
            }).ToList();
        var requests = role is FactionRole.Leader or FactionRole.Officer
            ? _factions.GetFactionJoinRequests(faction.ID).OrderBy(r => r.CreatedAt)
                .Select(r => new Wh40kFactionRequestUiSnapshot(r.PlayerUserId, r.LastSeenUserName)).ToList()
            : [];
        return new Wh40kFactionUiSnapshot(faction.ID, faction.Name, faction.Description, faction.Color.ToHex(),
            role, available, members, requests, accountAvailable, _bank.GetFactionBankSnapshot(session.UserId, faction.ID),
            GetSocialSnapshot(session, faction.ID), _factions.GetFactionRelations(),
            role == FactionRole.Leader ? _factions.GetFactionRelationCooldown(faction.ID) : 0);
    }

    private ProtoId<CompanyPrototype>? GetFaction(ICommonSession session)
    {
        var selected = (_preferences.GetPreferencesOrNull(session.UserId)?.SelectedCharacter as HumanoidCharacterProfile)?.Company;
        return _factions.GetActiveFaction(session.UserId, selected);
    }

    private void OnBankChanged(string faction)
    {
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.Disconnected &&
                FactionBankRules.CanManage(_factions.GetRole(session.UserId, faction)) && GetFaction(session)?.Id == faction)
                SendUpdatedSnapshot(session);
        }
    }

    private void OnFactionChanged(ProtoId<CompanyPrototype> faction, NetUserId affected)
    {
        foreach (var session in _players.Sessions)
        {
            if (session.Status == SessionStatus.Disconnected)
            {
                _lobbyViewers.Remove(session.UserId);
                continue;
            }
            SendUpdatedSnapshot(session);
        }
    }

    private void SendUpdatedSnapshot(ICommonSession session)
    {
        if (_lobbyViewers.Contains(session.UserId))
            RaiseNetworkEvent(new FactionLobbyStateEvent(CreateSnapshot(session), Wh40kFactionUiOperationStatus.None), session.Channel);
        if (session.AttachedEntity is not { Valid: true } actor)
            return;
        foreach (var (loader, key) in _ui.GetActorUis(actor))
        {
            if (PdaUiKey.Key.Equals(key) && TryComp(loader, out CartridgeLoaderComponent? cartridges) &&
                cartridges.ActiveProgram is { } program && HasComp<Wh40kFactionCartridgeComponent>(program))
                SendSnapshot(loader, actor, session, Wh40kFactionUiOperationStatus.None);
        }
    }

}
