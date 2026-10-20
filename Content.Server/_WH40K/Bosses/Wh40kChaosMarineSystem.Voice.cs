using System.Linq;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Mobs;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kChaosMarineSystem
{
    private const string VoicePath = "/Audio/_WH40K/Bosses/ChaosMarine/Voice/";

    private void InitializeVoice()
    {
        SubscribeLocalEvent<ActorComponent, MobStateChangedEvent>(OnPlayerDown);
        SubscribeLocalEvent<ActorComponent, BeforeDamageChangedEvent>(OnPlayerDamage);
    }

    private void OnPlayerDamage(EntityUid uid, ActorComponent actor, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || args.Origin is not { } source) return;
        var owner = MarineDamageOwner(source);
        if (owner is not { } boss) return;
        if (!CanTarget(boss, uid))
        {
            args.Cancelled = true;
            return;
        }
    }

    private EntityUid? MarineDamageOwner(EntityUid source) =>
        HasComp<Wh40kChaosMarineBrainComponent>(source) ? source :
        TryComp<Wh40kMarineMinionComponent>(source, out var minion) ? minion.Boss : null;

    private void OnPlayerDown(EntityUid uid, ActorComponent actor, MobStateChangedEvent args)
    {
        // Taunt the fall, including instant death, rather than taunting the same victim again on finishing.
        if (args.OldMobState != MobState.Alive || args.NewMobState is not (MobState.Critical or MobState.Dead) ||
            args.Origin is not { } source || !_npcs.Enabled) return;
        if (MarineDamageOwner(source) is { } boss && !TerminatingOrDeleted(boss) && !Paused(boss) &&
            !_mobs.IsDead(boss) && _arena.Contains(boss, uid) && !_factions.IsEntityFriendly(boss, uid) &&
            !HasComp<Content.Shared.Ghost.GhostComponent>(uid) && TryComp<Wh40kChaosMarineBrainComponent>(boss, out var brain) && !brain.Suspended)
            RequestVoice(boss, brain, 40);
    }

    private void RequestVoice(EntityUid uid, Wh40kChaosMarineBrainComponent brain, int priority)
    {
        if (priority == 40) brain.PendingDownVoice = true;
        brain.PendingVoice = Math.Max(brain.PendingVoice, priority);
        if (priority == 100) UpdateVoice(uid, brain);
    }

    private void UpdateVoice(EntityUid uid, Wh40kChaosMarineBrainComponent brain)
    {
        var priority = Math.Max(brain.PendingVoice, brain.PendingDownVoice ? 40 : 0);
        if (priority == 0) return;
        if (priority == 90 && TryComp<Wh40kChaosMarineComponent>(uid, out var state) &&
            Now < state.StartedAt + TimeSpan.FromSeconds(.6)) return;
        if (priority < 70 && (Now < brain.NextVoice || priority == 40 && Now < brain.NextKillVoice) ||
            Now < brain.VoiceUntil && priority <= brain.VoicePriority)
        {
            // Keep a down reaction until its turn; routine punch chatter can still be dropped.
            if (priority != 40) brain.PendingVoice = 0;
            return;
        }
        brain.PendingVoice = 0;
        if (priority is 40 or 100) brain.PendingDownVoice = false;
        string[] files = priority switch
        {
            100 => ["ah-argh-death.ogg"],
            90 => ["ha-ha-ha-ha-ha-ha-ha-ha-ha.ogg"],
            70 => ["support-us.ogg", "damnation-where-is-our-support.ogg"],
            40 => ["ha-ha.ogg", "die-scum.ogg"],
            _ => ["rap.ogg"],
        };
        var file = files[_random.Next(files.Length)];
        if (files.Length > 1 && file == brain.LastVoice) file = files[(Array.IndexOf(files, file) + 1) % files.Length];
        _audio.Stop(brain.Voice);
        var sound = new SoundPathSpecifier(VoicePath + file, AudioParams.Default.WithVolume(2)
            .WithMaxDistance(40).WithReferenceDistance(10).WithRolloffFactor(.5f));
        // A coordinate source lets the final vocalization outlive deletion of the body.
        var audience = Filter.Empty().AddPlayers(_players.Sessions.Where(s => s.AttachedEntity is { } player &&
            !HasComp<Content.Shared.Ghost.GhostComponent>(player) && _arena.Contains(uid, player)));
        brain.Voice = _audio.PlayStatic(sound, audience, Transform(uid).Coordinates, recordReplay: true);
        brain.LastVoice = file;
        brain.VoicePriority = priority;
        brain.VoiceUntil = Now + _audio.GetAudioLength(_audio.ResolveSound(sound));
        if (priority < 70) brain.NextVoice = Now + TimeSpan.FromSeconds(6);
        if (priority == 40) brain.NextKillVoice = Now + TimeSpan.FromSeconds(8);
    }
}
