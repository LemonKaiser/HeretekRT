using System.Linq;
using Content.Server.Advertise.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Server._WH40K.Dialogue;
using Content.Server._WH40K.Dialogue.Components;
using Content.Shared._WH40K.Npcs;
using Content.Shared.ActionBlocker;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Npcs;

/// <summary>Reuses advertisement scheduling, ordinary speech, human naming and hub protection.</summary>
public sealed partial class Wh40kAmbientNpcSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DialogueSystem _dialogue = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private GameTicker _ticker = default!;
    private readonly Dictionary<string, TimeSpan> _departmentSpeech = new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _globalSpeech;

    public override void Initialize()
    {
        SubscribeLocalEvent<Wh40kAmbientNpcSpawnComponent, MapInitEvent>(OnSpawn);
        SubscribeLocalEvent<Wh40kAmbientNpcSpawnComponent, ComponentShutdown>(OnSourceShutdown);
        SubscribeLocalEvent<Wh40kAmbientNpcComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<Wh40kAmbientNpcComponent, AttemptAdvertiseEvent>(OnAdvertisement);
        SubscribeLocalEvent<Wh40kAmbientNpcComponent, InteractHandEvent>(OnInteract);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
    }

    private void OnSpawn(EntityUid uid, Wh40kAmbientNpcSpawnComponent source, MapInitEvent args)
    {
        if (source.Body is {} body && !TerminatingOrDeleted(body)) return;
        if (!_config.GetCVar(CCVars.Wh40kAmbientNpcsEnabled) ||
            !_prototypes.TryIndex(source.Prototype, out var proto) ||
            !proto.TryGetComponent<Wh40kAmbientNpcComponent>(out var ambient, EntityManager.ComponentFactory) ||
            !_prototypes.TryIndex(ambient.Profile, out var profile) || profile.Kind == Wh40kNpcKind.Special ||
            profile.RequiresUniqueAnchor || Disabled(profile.Department)) return;
        var count = 0;
        var query = EntityQueryEnumerator<Wh40kAmbientNpcComponent>();
        while (query.MoveNext(out _, out _)) count++;
        if (count >= Math.Clamp(_config.GetCVar(CCVars.Wh40kAmbientNpcLimit), 0, 200)) return;
        var xform = Transform(uid);
        source.Body = SpawnAttachedTo(source.Prototype, xform.Coordinates, rotation: xform.LocalRotation);
    }

    private void OnSourceShutdown(EntityUid uid, Wh40kAmbientNpcSpawnComponent source, ComponentShutdown args)
    {
        if (source.Body is {} body && !TerminatingOrDeleted(body)) QueueDel(body);
    }

    private void OnInit(EntityUid uid, Wh40kAmbientNpcComponent comp, MapInitEvent args)
        => comp.NextSpeech = _timing.CurTime + TimeSpan.FromSeconds(1);

    private void OnRestart(RoundRestartCleanupEvent args)
    {
        _departmentSpeech.Clear();
        _globalSpeech = TimeSpan.Zero;
    }

    private bool Disabled(string department) => _config.GetCVar(CCVars.Wh40kAmbientDisabledDepartments)
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Contains(department, StringComparer.OrdinalIgnoreCase);

    private void OnAdvertisement(EntityUid uid, Wh40kAmbientNpcComponent comp, ref AttemptAdvertiseEvent args)
    {
        if (args.Cancelled) return;
        // AdvertiseSystem owns its queue/delays; replace only phrase selection and audience policy.
        args.Cancelled = true;
        TrySpeak(uid, comp);
    }

    private void OnInteract(EntityUid uid, Wh40kAmbientNpcComponent comp, InteractHandEvent args)
    {
        if (args.Handled || HasComp<DialogueInteractableComponent>(uid) || !HasComp<ActorComponent>(args.User) ||
            !_interaction.InRangeUnobstructed(args.User, uid, 2)) return;
        args.Handled = true;
        TrySpeak(uid, comp, direct: true);
    }

    /// <summary>Automatic speech uses shared budgets; direct replies only wait for this NPC's cooldown.</summary>
    public bool TrySpeak(EntityUid uid, Wh40kAmbientNpcComponent? comp = null, bool direct = false)
    {
        if (!Resolve(uid, ref comp) || !_config.GetCVar(CCVars.Wh40kAmbientNpcsEnabled) ||
            !_config.GetCVar(CCVars.Wh40kAmbientChatterEnabled) || _ticker.RunLevel != GameRunLevel.InRound ||
            Paused(uid) || !_mobState.IsAlive(uid) || !_blocker.CanSpeak(uid) ||
            !TryComp<HTNComponent>(uid, out var htn) || !htn.Enabled || !HasComp<ActiveNPCComponent>(uid) ||
            !_prototypes.TryIndex(comp.Profile, out var profile) || profile.Kind == Wh40kNpcKind.Special ||
            Disabled(profile.Department) || profile.Chatter is not {} pack || !_prototypes.TryIndex(pack, out var dataset)) return false;
        var now = _timing.CurTime;
        if (now < comp.NextSpeech) return false;
        if (!direct && (now < _globalSpeech || now < _departmentSpeech.GetValueOrDefault(profile.Department)))
            return false;
        var nearby = false;
        var point = _transform.GetMapCoordinates(uid);
        foreach (var (actorUid, actor) in _lookup.GetEntitiesInRange<ActorComponent>(point, 6))
        {
            var position = _transform.GetMapCoordinates(actorUid);
            if (position.MapId != point.MapId || (position.Position - point.Position).LengthSquared() > 36) continue;
            // A nearby player's active scene silences ambient bodies throughout this conversation area.
            if (_dialogue.IsInDialogue(actor.PlayerSession)) return false;
            if (_mobState.IsAlive(actorUid) && _interaction.InRangeUnobstructed(actorUid, uid, 6)) nearby = true;
        }
        if (!nearby) return false;
        var phrases = dataset.Values.Where(line => line != comp.LastPhrase).ToArray();
        if (phrases.Length == 0) phrases = dataset.Values.ToArray();
        if (phrases.Length == 0) return false;
        var phrase = _random.Pick(phrases);
        if (!Loc.TryGetString(phrase, out var message)) return false;
        _chat.TrySendInGameICMessage(uid, message, InGameICChatType.Speak, hideChat: false);
        comp.LastPhrase = phrase;
        comp.NextSpeech = now + TimeSpan.FromSeconds(direct ? 2 : 10);
        if (!direct)
        {
            _globalSpeech = now + TimeSpan.FromSeconds(Delay(CCVars.Wh40kAmbientChatterGlobalDelay));
            _departmentSpeech[profile.Department] = now + TimeSpan.FromSeconds(Delay(CCVars.Wh40kAmbientChatterDepartmentDelay));
        }
        return true;
    }

    private float Delay(CVarDef<float> setting)
    {
        var delay = _config.GetCVar(setting);
        return float.IsFinite(delay) ? Math.Clamp(delay, 0, 300) : setting.DefaultValue;
    }
}
