using System.Linq;
using Content.Server._WH40K.Dialogue;
using Content.Shared._WH40K.Npcs;
using Content.Shared._WH40K.Quests;
using Content.Shared.Cuffs.Components;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Quests;

public sealed partial class Wh40kQuestProgressSystem
{
    [Dependency] private IPrototypeManager _specialistPrototypes = default!;
    private readonly HashSet<EntityUid> _subjectSources = new();
    private readonly Dictionary<(Guid Quest, string Prototype), EntityUid> _subjects = new();

    private void RetireSpecialistRuntime(HashSet<Guid> closed)
    {
        if (closed.Count == 0) return;
        var workstations = EntityQueryEnumerator<Wh40kQuestWorkstationComponent>();
        while (workstations.MoveNext(out var uid, out var station)) station.CompletedQuests.RemoveWhere(closed.Contains);
        foreach (var (key, subject) in _subjects.Where(x => closed.Contains(x.Key.Quest)).ToArray())
        {
            _subjects.Remove(key);
            _treatedSubjects.Remove(subject);
            if (!TerminatingOrDeleted(subject) && TryComp(subject, out Wh40kQuestSubjectComponent? marker) && marker.QuestId == key.Quest &&
                !_players.TryGetSessionByEntity(subject, out _) && !(TryComp(subject, out MindContainerComponent? mind) && mind.Mind != null))
                QueueDel(subject);
        }
        foreach (var key in _eligibleTime.Keys.Where(x => closed.Contains(x.Quest)).ToArray()) _eligibleTime.Remove(key);
    }

    private void InitializeSubjects()
    {
        SubscribeLocalEvent<Wh40kQuestSubjectComponent, MapInitEvent>((uid, _, _) => EntityManager.System<Content.Server._WH40K.Npcs.Wh40kNpcProtectionSystem>().LockContents(uid));
        SubscribeLocalEvent<Wh40kQuestSubjectComponent, Robust.Shared.Containers.EntInsertedIntoContainerMessage>((uid, _, _) =>
            EntityManager.System<Content.Server._WH40K.Npcs.Wh40kNpcProtectionSystem>().LockContents(uid));
        SubscribeLocalEvent<Wh40kQuestSubjectSourceComponent, ComponentStartup>((uid, _, _) => _subjectSources.Add(uid));
        SubscribeLocalEvent<Wh40kQuestSubjectSourceComponent, ComponentShutdown>((uid, _, _) => _subjectSources.Remove(uid));
        SubscribeLocalEvent<Wh40kNpcIdentityComponent, InteractHandEvent>(OnSubjectIntake, before: [typeof(DialogueSystem)]);
        SubscribeLocalEvent<Wh40kQuestIntakeEvent>(OnExistingIntake);
        _sessions.AccountLoaded += BindSubjects;
    }

    private void BindSubjects(NetUserId user)
    {
        if (!_accounts.TryGetValue(user, out var rows)) return;
        foreach (var sub in rows.Where(x => (x.Count < x.Objective.RequiredCount || x.Objective.Kind is Wh40kQuestObjectiveKind.ProtectTarget or Wh40kQuestObjectiveKind.EscortEntity) && IsCurrentStep(user, x) &&
                     x.Objective.Target.Entity != null && _specialistPrototypes.Index<EntityPrototype>(x.Objective.Target.Entity).Components.ContainsKey("Wh40kQuestSubject")))
        {
            var key = (sub.Quest, sub.Objective.Target.Entity!);
            if (_subjects.TryGetValue(key, out var known) && Exists(known) && Comp<Wh40kQuestSubjectComponent>(known).Generation == _sessions.Generation) continue;
            if (_subjects.ContainsKey(key)) continue; // A lost/dead same-round subject is not automatically recreated.
            var sources = _subjectSources.Where(x => Exists(x) && Comp<Wh40kQuestSubjectSourceComponent>(x).Prototype.Id == key.Item2 &&
                Comp<Wh40kQuestSubjectSourceComponent>(x).CaseKey == sub.Objective.Target.CaseKey).Take(2).ToArray();
            if (sources.Length != 1) continue;
            var subject = Spawn(key.Item2, Transform(sources[0]).Coordinates);
            var marker = Comp<Wh40kQuestSubjectComponent>(subject);
            marker.QuestId = sub.Quest; marker.Generation = _sessions.Generation;
            _subjects[key] = subject;
        }
    }

    private bool SubjectSafe(EntityUid uid, Subscription sub)
        => !TerminatingOrDeleted(uid) && !_players.TryGetSessionByEntity(uid, out _) &&
           !(TryComp(uid, out MindContainerComponent? mind) && mind.Mind != null) &&
           TryComp(uid, out Wh40kQuestSubjectComponent? marker) && marker.QuestId == sub.Quest && marker.Generation == _sessions.Generation &&
           marker.CaseKey == sub.Objective.Target.CaseKey && Matches(uid, sub.Objective.Target, sub.Quest, sub.Objective.Id) &&
           TryComp(uid, out MobStateComponent? state) && state.CurrentState != MobState.Dead;

    private void OnSubjectIntake(EntityUid npc, Wh40kNpcIdentityComponent component, InteractHandEvent ev)
    {
        if (_pending.Count >= 2048 || !TryOwner(ev.User, out var user) || !_anchors.TryGetIdentity(npc, out var identity) || !_anchors.IsInRange(ev.User, npc)) return;
        foreach (var sub in _accounts[user].Where(x => x.Objective.Kind is Wh40kQuestObjectiveKind.DeliverEntity or
                     Wh40kQuestObjectiveKind.RescueAndDeliver or Wh40kQuestObjectiveKind.CaptureAlive && IsCurrentStep(user, x) &&
                     x.Count < x.Objective.RequiredCount && x.Objective.Target.Destination == identity.Id))
        {
            if (!_subjects.TryGetValue((sub.Quest, sub.Objective.Target.Entity!), out var subject) || !SubjectSafe(subject, sub) ||
                !_interaction.InRangeUnobstructed(ev.User, subject) || !_interaction.InRangeUnobstructed(npc, subject)) continue;
            if (sub.Objective.Kind == Wh40kQuestObjectiveKind.CaptureAlive &&
                (!TryComp(subject, out CuffableComponent? cuffs) || cuffs.CuffedHandCount < 2)) continue;
            if (sub.Objective.Kind == Wh40kQuestObjectiveKind.RescueAndDeliver && Comp<MobStateComponent>(subject).CurrentState != MobState.Alive) continue;
            ObserveSpecialist(user, sub, 1, $"{_sessions.Generation:N}:{Comp<Wh40kQuestSubjectComponent>(subject).Token:N}");
            QueueDel(subject); // Only this contract's disposable, unowned mission body.
            ev.Handled = true;
        }
    }

    private void OnExistingIntake(Wh40kQuestIntakeEvent ev)
    {
        if (ev.Actor is not {} actor || !TryOwner(actor, out var user) || !_interaction.InRangeUnobstructed(actor, ev.Receiver)) return;
        foreach (var sub in _accounts[user].Where(x => x.Objective.Kind is Wh40kQuestObjectiveKind.DeliverEntity or Wh40kQuestObjectiveKind.RescueAndDeliver &&
                     x.Count < x.Objective.RequiredCount && IsCurrentStep(user, x) && Matches(ev.Root, x.Objective.Target, x.Quest, x.Objective.Id) &&
                     x.Objective.Target.Destination != null && _anchors.TryGetNpc(x.Objective.Target.Destination, out var npc) && _anchors.IsInRange(ev.Receiver, npc)))
            ObserveSpecialist(user, sub, 1, $"{_sessions.Generation:N}:{EnsureComp<Wh40kQuestVictimTokenComponent>(ev.Root).Token:N}");
    }

    private void UpdateSubjects()
    {
        var seen = new HashSet<(Guid, string)>();
        foreach (var (user, rows) in _accounts)
        {
            if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not {} body || !TryOwner(body, out _) ||
                !TryComp(body, out MobStateComponent? actorState) || actorState.CurrentState != MobState.Alive) continue;
            foreach (var sub in rows.Where(x => x.Objective.Kind is Wh40kQuestObjectiveKind.EscortEntity or Wh40kQuestObjectiveKind.ProtectTarget &&
                         IsCurrentStep(user, x)))
            {
                if (seen.Contains((sub.Quest, sub.Objective.Id)) || !_subjects.TryGetValue((sub.Quest, sub.Objective.Target.Entity!), out var target)) continue;
                if (!SubjectSafe(target, sub))
                {
                    seen.Add((sub.Quest, sub.Objective.Id));
                    if (TerminatingOrDeleted(target) || TryComp(target, out MobStateComponent? state) && state.CurrentState == MobState.Dead)
                        _sessions.TrackWork(_sessions.ResolveForAdminAsync(sub.Snapshot.Owner, user.UserId,
                            new(Guid.NewGuid(), Wh40kQuestMutation.AdminFail, sub.Quest, _sessions.TryGetStatus(user, out var statuses) ?
                                statuses.Single(x => x.Contract.Id == sub.Quest).Contract.Revision : 0, "Bound mission subject lost.")));
                    continue;
                }
                if (sub.Count >= sub.Objective.RequiredCount) continue;
                if (!_interaction.InRangeUnobstructed(body, target, sub.Objective.Target.Radius)) continue;
                seen.Add((sub.Quest, sub.Objective.Id));
                if (sub.Objective.Kind == Wh40kQuestObjectiveKind.EscortEntity)
                {
                    if (sub.Objective.Target.Destination != null && _anchors.TryGetNpc(sub.Objective.Target.Destination, out var destination) &&
                        _anchors.IsInRange(target, destination))
                        ObserveSpecialist(user, sub, 1, $"{_sessions.Generation:N}:{Comp<Wh40kQuestSubjectComponent>(target).Token:N}");
                }
                else ObserveSpecialist(user, sub, 1, $"{_sessions.Generation:N}:protect:{++_specialistSequence}");
            }
        }
    }
}
