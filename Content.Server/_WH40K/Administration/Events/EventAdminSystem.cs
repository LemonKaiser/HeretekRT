using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Shared._WH40K.Administration.Events;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Bed.Sleep;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Administration.Events;

/// <summary>
/// Round-scoped event rosters and rally points, shared by all event administrators.
/// Membership follows accounts; return locations and temporary effects follow the actual body.
/// </summary>
public sealed partial class EventAdminSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IAdminAuthorizationManager _authorization = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IAdminLogManager _logs = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private readonly Dictionary<int, Team> _teams = new();
    private readonly Dictionary<int, Point> _points = new();
    private readonly Dictionary<Guid, int> _members = new();
    private readonly Dictionary<Guid, string> _names = new();
    private int _nextId;
    private int _epoch = 1;
    private int _permissionRevision;
    private TimeSpan _nextRefresh;
    private TimeSpan? _deadline;
    private int _pausedSeconds;
    public event Action? Changed;

    private sealed class Team(string name)
    {
        public string Name = name;
        public int Score;
        public bool FriendlyFire = true;
    }

    private sealed record Point(string Name, EntityCoordinates Coordinates);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset(roundCleanup: true));
        _admins.OnPermsChanged += OnPermissionsChanged;
    }

    public override void Shutdown()
    {
        _admins.OnPermsChanged -= OnPermissionsChanged;
        base.Shutdown();
    }

    private void OnPermissionsChanged(Content.Server.Administration.AdminPermsChangedEventArgs args)
    {
        _permissionRevision++;
        Changed?.Invoke();
    }

    public bool Authorized(ICommonSession actor) => actor.Status != SessionStatus.Disconnected &&
        _admins.HasAdminFlag(actor, AdminFlags.Fun);

    public EventAdminEuiState Snapshot(ICommonSession actor)
    {
        if (!Authorized(actor))
            return new EventAdminEuiState();
        var roster = new Dictionary<Guid, EventAdminPlayer>();
        foreach (var session in _players.Sessions)
        {
            if (session.Status == SessionStatus.Disconnected)
                continue;
            var id = session.UserId.UserId;
            var body = session.AttachedEntity;
            var hasBody = body is { } uid && !TerminatingOrDeleted(uid);
            _names[id] = session.Name;
            roster[id] = new EventAdminPlayer(id, session.Name, hasBody ? Name(body!.Value) : "",
                _members.GetValueOrDefault(id, -1), true, hasBody, hasBody && _mobState.IsDead(body!.Value),
                hasBody && HasComp<AdminFrozenComponent>(body), hasBody && HasComp<GodmodeComponent>(body),
                hasBody && HasComp<SleepingComponent>(body), hasBody && _returns.TryGetValue(body!.Value, out var origin) && ValidPoint(origin),
                hasBody && HasComp<GhostComponent>(body));
        }
        foreach (var (id, team) in _members)
            roster.TryAdd(id, new EventAdminPlayer(id, _names.GetValueOrDefault(id, id.ToString()), "", team,
                false, false, false, false, false, false, false));
        return new EventAdminEuiState
        {
            Epoch = _epoch,
            AllowedActions = Enum.GetValues<EventAdminAction>().Where(a => _admins.HasAdminFlag(actor, EventAdminAccess.Required(a))).ToArray(),
            Players = roster.Values.OrderBy(p => p.Account, StringComparer.OrdinalIgnoreCase).ToArray(),
            Teams = _teams.Select(t => new EventAdminTeam(t.Key, t.Value.Name, t.Value.Score,
                _members.Count(m => m.Value == t.Key), t.Value.FriendlyFire)).ToArray(),
            Points = _points.Select(p => new EventAdminPoint(p.Key, p.Value.Name, ValidPoint(p.Value.Coordinates))).ToArray(),
            SecondsLeft = SecondsLeft,
            TimerRunning = _deadline != null,
        };
    }

    private int SecondsLeft => _deadline is { } deadline
        ? Math.Max(0, (int) Math.Ceiling((deadline - _timing.CurTime).TotalSeconds)) : _pausedSeconds;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_deadline is { } deadline && deadline <= _timing.CurTime)
        {
            _deadline = null;
            _pausedSeconds = 0;
            foreach (var id in _members.Keys)
                if (_players.TryGetSessionById(new NetUserId(id), out var session) && session.Status != SessionStatus.Disconnected)
                    _chat.DispatchServerMessage(session, Loc.GetString("wh40k-event-timer-finished"));
            Changed?.Invoke();
        }
        if (Changed == null || _timing.CurTime < _nextRefresh)
            return;
        _nextRefresh = _timing.CurTime + TimeSpan.FromSeconds(1);
        Changed.Invoke();
    }

    public async Task<EventAdminResult> Execute(ICommonSession actor, EventAdminRequest request)
    {
        if (!Authorized(actor) || !_admins.HasAdminFlag(actor, EventAdminAccess.Required(request.Action)))
            return new("wh40k-event-denied");
        if (request.Epoch != _epoch)
            return new("wh40k-event-stale");
        if (!Enum.IsDefined(request.Action) || request.Players.Length > EventAdminLimits.Targets || request.Text.Length > 2048)
            return new("wh40k-event-invalid");
        if (request.Action == EventAdminAction.Refresh)
            return new();

        var epoch = _epoch;
        var permissionRevision = _permissionRevision;
        var requested = Targets(request);
        var targets = new List<Guid>();
        var skipped = 0;
        foreach (var id in requested)
        {
            if (id == Guid.Empty || !_players.TryGetSessionById(new NetUserId(id), out _) && !_members.ContainsKey(id))
            {
                skipped++;
                continue;
            }
            if (await _authorization.TryDenyTargetAsync(actor, new NetUserId(id), AdminOperation.GenericTarget,
                    _players.TryGetSessionById(new NetUserId(id), out var targetSession) ? targetSession.Name : _names.GetValueOrDefault(id)))
                skipped++;
            else
                targets.Add(id);
            if (!Authorized(actor) || !_admins.HasAdminFlag(actor, EventAdminAccess.Required(request.Action)))
                return new("wh40k-event-denied");
            if (epoch != _epoch || permissionRevision != _permissionRevision)
                return new("wh40k-event-stale");
        }

        var organisationalRemoval = request.Action is EventAdminAction.DeleteTeam or EventAdminAction.Reset;
        var result = organisationalRemoval && !requested.ToHashSet().SetEquals(Targets(request))
            ? new EventAdminResult("wh40k-event-stale")
            : organisationalRemoval && skipped > 0 ? new EventAdminResult("wh40k-event-protected-team", Skipped: skipped)
            : Apply(actor, request, targets, skipped);
        _logs.Add(LogType.AdminCommands, LogImpact.High,
            $"Event panel {actor.Name} ({actor.UserId}): {request.Action}; team={request.Team}, point={request.Point}, " +
            $"requested={string.Join(",", requested)}; text={request.Text}; number={request.Number}; result={result.Key}/{result.Applied}/{result.Skipped}");
        Changed?.Invoke();
        return result;
    }

    private Guid[] Targets(EventAdminRequest request) => request.Action switch
    {
        EventAdminAction.DeleteTeam => _members.Where(m => m.Value == request.Team).Select(m => m.Key).ToArray(),
        EventAdminAction.Reset => _members.Keys.ToArray(),
        _ => EventAdminAccess.UsesSelection(request.Action) ? request.Players.Distinct().ToArray() : [],
    };

    private EventAdminResult Apply(ICommonSession actor, EventAdminRequest request, List<Guid> targets, int skipped)
    {
        var text = request.Text.Trim();
        switch (request.Action)
        {
            case EventAdminAction.CreateTeam:
                if (_teams.Count >= EventAdminLimits.Teams || !ValidName(text) || _teams.Values.Any(t => t.Name.Equals(text, StringComparison.OrdinalIgnoreCase)))
                    return new("wh40k-event-invalid-name");
                _teams.Add(++_nextId, new Team(text));
                return Done(1);
            case EventAdminAction.RenameTeam:
                if (!_teams.TryGetValue(request.Team, out var rename) || !ValidName(text) ||
                    _teams.Any(t => t.Key != request.Team && t.Value.Name.Equals(text, StringComparison.OrdinalIgnoreCase)))
                    return new("wh40k-event-invalid-name");
                rename.Name = text;
                return Done(1);
            case EventAdminAction.DeleteTeam:
                if (!_teams.Remove(request.Team)) return Invalid();
                foreach (var id in _members.Where(m => m.Value == request.Team).Select(m => m.Key).ToArray())
                    _members.Remove(id);
                return Done(1);
            case EventAdminAction.SetScore:
                if (!_teams.TryGetValue(request.Team, out var score) || Math.Abs((long) request.Number) > 99999) return Invalid();
                score.Score = request.Number;
                return Done(1);
            case EventAdminAction.FriendlyFireOn:
            case EventAdminAction.FriendlyFireOff:
                if (!_teams.TryGetValue(request.Team, out var team)) return Invalid();
                team.FriendlyFire = request.Action == EventAdminAction.FriendlyFireOn;
                return Done(1);
            case EventAdminAction.AssignTeam:
            case EventAdminAction.UnassignTeam:
            case EventAdminAction.BalanceTeams:
                if (targets.Count == 0) return Done(0, skipped);
                if (request.Action == EventAdminAction.AssignTeam && !_teams.ContainsKey(request.Team) ||
                    request.Action == EventAdminAction.BalanceTeams && _teams.Count < 2) return Invalid();
                if (request.Action == EventAdminAction.BalanceTeams)
                {
                    _random.Shuffle(targets);
                    foreach (var id in targets) _members.Remove(id);
                }
                foreach (var id in targets)
                {
                    if (_players.TryGetSessionById(new NetUserId(id), out var session)) _names[id] = session.Name;
                    if (request.Action == EventAdminAction.UnassignTeam)
                        _members.Remove(id);
                    else
                        _members[id] = request.Action == EventAdminAction.AssignTeam ? request.Team :
                            _teams.Keys.MinBy(t => _members.Count(m => m.Value == t));
                }
                return Done(targets.Count, skipped);
            case EventAdminAction.CreatePoint:
                if (_points.Count >= EventAdminLimits.Points || !ValidName(text) || !TryActorPosition(actor, out var position) ||
                    _points.Values.Any(p => p.Name.Equals(text, StringComparison.OrdinalIgnoreCase))) return Invalid();
                _points.Add(++_nextId, new Point(text, position));
                return Done(1);
            case EventAdminAction.DeletePoint:
                return _points.Remove(request.Point) ? Done(1) : Invalid();
            case EventAdminAction.StartTimer:
                if (request.Number is < 1 or > EventAdminLimits.TimerSeconds) return Invalid();
                _pausedSeconds = 0;
                _deadline = _timing.CurTime + TimeSpan.FromSeconds(request.Number);
                return Done(1);
            case EventAdminAction.PauseTimer:
                _pausedSeconds = SecondsLeft;
                _deadline = null;
                return Done(1);
            case EventAdminAction.ResumeTimer:
                if (_deadline != null || _pausedSeconds <= 0) return Invalid();
                _deadline = _timing.CurTime + TimeSpan.FromSeconds(_pausedSeconds);
                _pausedSeconds = 0;
                return Done(1);
            case EventAdminAction.StopTimer:
                _deadline = null;
                _pausedSeconds = 0;
                return Done(1);
            case EventAdminAction.Announce:
                if (text.Length is < 1 or > EventAdminLimits.MessageLength) return Invalid();
                _chat.DispatchServerAnnouncement(text);
                return Done(1);
            case EventAdminAction.Reset:
                // Explicitly resets event organisation, not players' bodies or unrelated admin effects.
                Reset();
                return Done(1);
            default:
                return ApplyToPlayers(actor, request, targets, skipped);
        }
    }

    private static bool ValidName(string name) => name.Length is >= 1 and <= EventAdminLimits.NameLength && !name.Any(char.IsControl);
    private bool ValidPoint(EntityCoordinates point) => point.IsValid(EntityManager) &&
        Transform(point.EntityId).MapUid != null;
    private bool TryActorPosition(ICommonSession actor, out EntityCoordinates position)
    {
        position = default;
        if (actor.AttachedEntity is not { } uid || TerminatingOrDeleted(uid)) return false;
        position = Transform(uid).Coordinates;
        return ValidPoint(position);
    }
    private static EventAdminResult Done(int count, int skipped = 0) => new("wh40k-event-result", count, skipped);
    private static EventAdminResult Invalid() => new("wh40k-event-invalid");

    private void Reset(bool roundCleanup = false)
    {
        _epoch++;
        _teams.Clear(); _members.Clear(); _names.Clear(); _points.Clear();
        if (roundCleanup) { _returns.Clear(); _effects.Clear(); }
        _deadline = null;
        _pausedSeconds = 0;
        Changed?.Invoke();
    }
}
