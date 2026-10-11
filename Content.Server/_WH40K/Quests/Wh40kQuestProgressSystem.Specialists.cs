using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Content.Server.GameTicking;
using Content.Shared._WH40K.Quests;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mining.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Repairable;
using Content.Shared.Stacks;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Quests;

public sealed partial class Wh40kQuestProgressSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    private readonly Dictionary<EntityUid, EntityUid> _miners = new();
    private readonly Dictionary<EntityUid, NetUserId> _treatedSubjects = new();
    private readonly Dictionary<(Guid Quest, string Objective), float> _eligibleTime = new();
    private float _specialistTimer;
    private long _specialistSequence;

    private void InitializeSpecialists()
    {
        InitializeSubjects();
        SubscribeLocalEvent<Wh40kQuestWorkstationComponent, InteractHandEvent>(OnWorkHand, before: [typeof(Content.Server._WH40K.Dialogue.DialogueSystem)]);
        SubscribeLocalEvent<Wh40kQuestWorkstationComponent, InteractUsingEvent>(OnWorkUsing);
        SubscribeLocalEvent<Wh40kQuestWorkstationComponent, Wh40kQuestWorkDoAfterEvent>(OnWorkFinished);
        SubscribeLocalEvent<RepairableComponent, RepairedEvent>(OnRepairObserved);
        SubscribeLocalEvent<Wh40kQuestSubjectComponent, DamageChangedEvent>(OnHealingObserved);
        SubscribeLocalEvent<OreVeinComponent, DamageChangedEvent>(OnMiningDamage);
        SubscribeLocalEvent<Wh40kQuestExtractionEvent>(OnExtraction);
        SubscribeLocalEvent<Wh40kQuestActivityOutcomeEvent>(OnActivityOutcome);
        SubscribeLocalEvent<Wh40kQuestAccountAccessComponent, GetAccessTagsEvent>(OnAccountAccess);
        _sessions.AccountLoaded += ApplyAccountAccess;
    }

    private void ApplyAccountAccess(NetUserId user)
    {
        if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not {} body) return;
        var access = EnsureComp<Wh40kQuestAccountAccessComponent>(body);
        access.Tags.Clear();
        foreach (var state in _sessions.AccountState(user).Where(x => x.Key.StartsWith("access:") && x.Amount > 0))
            access.Tags.Add(new(state.Key[7..]));
    }

    private void OnAccountAccess(EntityUid uid, Wh40kQuestAccountAccessComponent component, ref GetAccessTagsEvent ev)
    {
        if (TryOwner(uid, out _)) ev.Tags.UnionWith(component.Tags);
    }

    private IEnumerable<Subscription> SpecialistSubscriptions(NetUserId user, Wh40kQuestObjectiveKind kind)
        => _accounts.GetValueOrDefault(user)?.Where(x => x.Objective.Kind == kind && x.Count < x.Objective.RequiredCount && IsCurrentStep(user, x)) ?? [];

    private void ObserveSpecialist(NetUserId user, Subscription sub, int amount, string token,
        Wh40kQuestActivityBinding? activity = null)
    {
        if (_pending.Count >= 2048) return;
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{sub.Quest:N}:{sub.Objective.Id}:{token}")).AsSpan(0, 16));
        if (_pending.ContainsKey(id)) return;
        var fact = new Wh40kQuestFact(id, sub.Objective.Id, Math.Min(amount, sub.Objective.RequiredCount), false,
            DateTime.UtcNow, token, activity, user.UserId);
        _pending.Add(id, new(sub.Snapshot.Owner, sub.Quest, fact));
    }

    private void OnWorkHand(EntityUid uid, Wh40kQuestWorkstationComponent component, InteractHandEvent ev)
    {
        if (ev.Handled || component.SourceItem != null) return;
        ev.Handled = BeginSpecialistWork(uid, component, ev.User, null);
    }

    private void OnWorkUsing(EntityUid uid, Wh40kQuestWorkstationComponent component, InteractUsingEvent ev)
    {
        if (ev.Handled || MetaData(ev.Used).EntityPrototype?.ID != component.SourceItem?.Id) return;
        ev.Handled = BeginSpecialistWork(uid, component, ev.User, ev.Used);
    }

    private bool BeginSpecialistWork(EntityUid uid, Wh40kQuestWorkstationComponent component, EntityUid actor, EntityUid? used)
    {
        if (!TryOwner(actor, out var user) || !_interaction.InRangeUnobstructed(actor, uid) ||
            !float.IsFinite(component.Delay) || component.Delay is < 0.1f or > 60 ||
            !SpecialistSubscriptions(user, component.Kind).Any(x => WorkMatches(uid, component, x))) return false;
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, actor, component.Delay, new Wh40kQuestWorkDoAfterEvent(), uid,
            target: uid, used: used) { BreakOnMove = true, BreakOnDamage = true, NeedHand = true });
    }

    private bool WorkMatches(EntityUid uid, Wh40kQuestWorkstationComponent component, Subscription sub)
        => !component.CompletedQuests.Contains(sub.Quest) &&
           (sub.Objective.Kind == Wh40kQuestObjectiveKind.CollectEvidence
               ? component.CaseKey == sub.Objective.Target.CaseKey && component.Evidence?.Id == sub.Objective.Target.Entity
               : Matches(uid, sub.Objective.Target, sub.Quest, sub.Objective.Id));

    private void OnWorkFinished(EntityUid uid, Wh40kQuestWorkstationComponent component, Wh40kQuestWorkDoAfterEvent ev)
    {
        if (ev.Cancelled || ev.Handled || !TryOwner(ev.User, out var user) || !_interaction.InRangeUnobstructed(ev.User, uid)) return;
        if (component.SourceItem != null && (ev.Used is not {} used || !_hands.IsHolding(ev.User, used, out _) ||
            MetaData(used).EntityPrototype?.ID != component.SourceItem.Value.Id)) return;
        var subscriptions = SpecialistSubscriptions(user, component.Kind).Where(x => WorkMatches(uid, component, x)).ToArray();
        if (component.Kind is Wh40kQuestObjectiveKind.InstallComponent or Wh40kQuestObjectiveKind.CollectEvidence)
            subscriptions = subscriptions.Take(1).ToArray();
        if (subscriptions.Length == 0 || _pending.Count + subscriptions.Length > 2048) return;
        if (component.Kind == Wh40kQuestObjectiveKind.InstallComponent && ev.Used is {} installed)
        {
            var stackSystem = EntityManager.System<SharedStackSystem>();
            if (TryComp(installed, out StackComponent? stack))
            { if (!stackSystem.Use(installed, 1, stack)) return; }
            else { if (!_hands.TryDrop(ev.User, installed)) return; QueueDel(installed); }
        }
        ev.Handled = true;
        foreach (var sub in subscriptions)
        {
            component.CompletedQuests.Add(sub.Quest);
            if (component.Kind == Wh40kQuestObjectiveKind.CollectEvidence)
            {
                _sessions.TrackWork(IssueEvidenceAsync(user, ev.User, uid, component, sub, _sessions.Generation));
            }
            else ObserveSpecialist(user, sub, 1, $"{_sessions.Generation:N}:{EnsureComp<Wh40kQuestVictimTokenComponent>(uid).Token:N}");
        }
    }

    private async Task IssueEvidenceAsync(NetUserId user, EntityUid actor, EntityUid station,
        Wh40kQuestWorkstationComponent component, Subscription sub, Guid generation)
    {
        try
        {
            if (!await _db.IssueWh40kQuestEvidenceAsync(sub.Snapshot.Owner, user.UserId, sub.Quest, sub.Objective.Id)) return;
            await _sessions.OnSimulationThread(() =>
            {
                if (generation != _sessions.Generation || !TryOwner(actor, out var current) || current != user ||
                    TerminatingOrDeleted(station) || !_interaction.InRangeUnobstructed(actor, station)) return;
                var item = Spawn(component.Evidence!.Value, Transform(actor).Coordinates);
                var provenance = EnsureComp<Wh40kQuestItemComponent>(item);
                provenance.QuestId = sub.Quest; provenance.ObjectiveId = sub.Objective.Id;
                _hands.TryPickupAnyHand(actor, item);
            });
        }
        catch (Exception error)
        {
            Log.Error($"Evidence issuance failed; inspect durable custody before replacing it: {error}");
        }
    }

    private void OnRepairObserved(EntityUid uid, RepairableComponent component, ref RepairedEvent ev)
    {
        if (!TryOwner(ev.User, out var user)) return;
        var target = ev.Ent.Owner;
        foreach (var sub in SpecialistSubscriptions(user, Wh40kQuestObjectiveKind.RepairEntity).Where(x => Matches(target, x.Objective.Target, x.Quest, x.Objective.Id)))
            ObserveSpecialist(user, sub, 1, $"{_sessions.Generation:N}:{EnsureComp<Wh40kQuestVictimTokenComponent>(target).Token:N}");
    }

    private void OnHealingObserved(EntityUid uid, Wh40kQuestSubjectComponent component, DamageChangedEvent ev)
    {
        if (ev.Origin is not {} actor || !TryOwner(actor, out var user) || ev.DamageDelta == null ||
            ev.DamageDelta.GetTotal() >= 0 || !TryComp(uid, out MobStateComponent? state) || state.CurrentState == MobState.Dead ||
            !SpecialistSubscriptions(user, Wh40kQuestObjectiveKind.StabilizeTarget).Any(x => SubjectSafe(uid, x))) return;
        _treatedSubjects[uid] = user;
    }

    private void OnMiningDamage(EntityUid uid, OreVeinComponent component, DamageChangedEvent ev)
    {
        if (ev.Origin is {} actor && ev.DamageIncreased && TryOwner(actor, out _)) _miners[uid] = actor;
    }

    private void OnExtraction(Wh40kQuestExtractionEvent ev)
    {
        var actor = ev.Actor ?? (_miners.TryGetValue(ev.Vein, out var miner) ? miner : (EntityUid?) null);
        if (actor is not {} body || !TryOwner(body, out var user) || ev.Amount <= 0) return;
        foreach (var sub in SpecialistSubscriptions(user, Wh40kQuestObjectiveKind.ExtractResource).Where(x =>
                     x.Objective.Target.Kind == Wh40kQuestTargetKind.Entity && x.Objective.Target.Entity == ev.Ore))
            ObserveSpecialist(user, sub, ev.Amount, $"{_sessions.Generation:N}:{EnsureComp<Wh40kQuestVictimTokenComponent>(ev.Vein).Token:N}");
        _miners.Remove(ev.Vein);
    }

    private void OnActivityOutcome(Wh40kQuestActivityOutcomeEvent ev)
    {
        if (ev.Actor is not {} actor || !TryOwner(actor, out var user)) return;
        var binding = new Wh40kQuestActivityBinding(_sessions.Generation, EntityManager.System<GameTicker>().RoundId, ev.Instance);
        foreach (var kind in new[] { Wh40kQuestObjectiveKind.CompleteActivity, Wh40kQuestObjectiveKind.RecoverActivityObjective })
        foreach (var sub in SpecialistSubscriptions(user, kind).Where(x => x.Objective.Target.Activity == ev.Template &&
                     (x.Objective.Target.System == null || x.Objective.Target.System == ev.System) &&
                     x.Objective.Target.TerminalReason == ev.Reason && (kind != Wh40kQuestObjectiveKind.RecoverActivityObjective || ev.Recovered != null)))
            ObserveSpecialist(user, sub, 1, $"{binding.ServerGeneration:N}:{binding.InstanceId}", binding);
    }

    private void UpdateSpecialists(float frameTime)
    {
        _specialistTimer += frameTime;
        if (_specialistTimer < 1) return;
        var elapsed = Math.Min(_specialistTimer, 1f); _specialistTimer = 0;
        foreach (var (subject, healer) in _treatedSubjects.ToArray())
        {
            if (TerminatingOrDeleted(subject) || !TryComp(subject, out MobStateComponent? state) || state.CurrentState == MobState.Dead)
            { _treatedSubjects.Remove(subject); continue; }
            if (state.CurrentState != MobState.Alive) continue;
            foreach (var sub in SpecialistSubscriptions(healer, Wh40kQuestObjectiveKind.StabilizeTarget).Where(x => SubjectSafe(subject, x)))
                ObserveSpecialist(healer, sub, 1, $"{_sessions.Generation:N}:{Comp<Wh40kQuestSubjectComponent>(subject).Token:N}");
            _treatedSubjects.Remove(subject);
        }
        UpdateSubjects();
        var seen = new HashSet<(Guid, string)>();
        foreach (var (user, subscriptions) in _accounts)
        {
            if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not {} body || !TryOwner(body, out _) ||
                !TryComp(body, out MobStateComponent? alive) || alive.CurrentState != MobState.Alive) continue;
            foreach (var sub in subscriptions.Where(x => x.Objective.Kind == Wh40kQuestObjectiveKind.SurviveDuration && IsCurrentStep(user, x)))
            {
                var key = (sub.Quest, sub.Objective.Id);
                if (!seen.Add(key) || sub.Count >= sub.Objective.RequiredCount || !Matches(body, sub.Objective.Target, sub.Quest, sub.Objective.Id)) continue;
                var time = _eligibleTime.GetValueOrDefault(key) + elapsed;
                _eligibleTime[key] = time;
                if (time >= 1) { _eligibleTime[key] -= 1; ObserveSpecialist(user, sub, 1, $"{_sessions.Generation:N}:duration:{++_specialistSequence}"); }
            }
        }
    }
}
