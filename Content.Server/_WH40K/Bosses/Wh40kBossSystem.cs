using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Bosses;

public sealed partial class Wh40kBossSystem : EntitySystem
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private Wh40kBossArenaSystem _arena = default!;

    private readonly Dictionary<ICommonSession, EntityUid> _recipients = new();
    private readonly Dictionary<EntityUid, ResolvedSoundSpecifier?> _themes = new();
    private readonly HashSet<ICommonSession> _connected = new();
    private readonly List<ICommonSession> _removed = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kBossComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kBossComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kBossComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
    }

    private void OnStartup(EntityUid uid, Wh40kBossComponent component, ComponentStartup args)
    {
        _themes[uid] = component.Theme == null ? null : _audio.ResolveSound(component.Theme);
        _nextUpdate = TimeSpan.Zero;
    }

    private void OnShutdown(EntityUid uid, Wh40kBossComponent component, ComponentShutdown args)
    {
        _themes.Remove(uid);
        _nextUpdate = TimeSpan.Zero;
    }

    private void OnMobState(EntityUid uid, Wh40kBossComponent component, MobStateChangedEvent args)
    {
        _nextUpdate = TimeSpan.Zero;
    }

    private void OnRestart(RoundRestartCleanupEvent args)
    {
        _recipients.Clear();
        _themes.Clear();
        _nextUpdate = TimeSpan.Zero;
    }

    public bool TryGetHealth(EntityUid boss, out float health, out float maximum)
    {
        health = maximum = 0;
        if (!TryComp<DamageableComponent>(boss, out var damage) ||
            !_thresholds.TryGetThresholdForState(boss, MobState.Dead, out var threshold))
            return false;

        maximum = threshold.Value.Float();
        if (maximum <= 0)
            return false;

        health = _mobState.IsDead(boss) ? 0 : Math.Clamp(maximum - damage.TotalDamage.Float(), 0, maximum);
        return true;
    }

    public bool IsInAudience(EntityUid boss, Wh40kBossComponent component, EntityUid player)
    {
        if (!component.Enabled || !float.IsFinite(component.AwarenessRadius) || component.AwarenessRadius <= 0 ||
            TerminatingOrDeleted(boss) || Paused(boss) || _mobState.IsDead(boss) ||
            HasComp<GhostComponent>(player) || !HasComp<MobStateComponent>(player) || _mobState.IsDead(player) ||
            !TryComp(boss, out TransformComponent? bossTransform) ||
            !TryComp(player, out TransformComponent? playerTransform) ||
            bossTransform.MapID == MapId.Nullspace || bossTransform.MapID != playerTransform.MapID ||
            component.SameGridOnly && (bossTransform.GridUid == null || bossTransform.GridUid != playerTransform.GridUid))
            return false;

        if (HasComp<Wh40kBossArenaComponent>(boss))
            return _arena.Contains(boss, player);
        return Vector2.DistanceSquared(_transform.GetWorldPosition(bossTransform),
            _transform.GetWorldPosition(playerTransform)) <= component.AwarenessRadius * component.AwarenessRadius;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        _connected.Clear();
        foreach (var session in _players.Sessions)
        {
            _connected.Add(session);
            EntityUid? selected = null;
            Wh40kBossComponent? selectedComponent = null;
            var bestDistance = float.MaxValue;
            var previous = _recipients.GetValueOrDefault(session);
            if (session.AttachedEntity is { } player)
            {
                var query = EntityQueryEnumerator<Wh40kBossComponent, TransformComponent>();
                while (query.MoveNext(out var boss, out var component, out var xform))
                {
                    if (!IsInAudience(boss, component, player) || !TryGetHealth(boss, out _, out _))
                        continue;

                    var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(xform), _transform.GetWorldPosition(player));
                    // Keep an equally important encounter until another is substantially closer.
                    if (boss == previous)
                        distance *= 0.8f;
                    if (selectedComponent != null && (component.Priority < selectedComponent.Priority ||
                        component.Priority == selectedComponent.Priority && distance >= bestDistance))
                        continue;
                    selected = boss;
                    selectedComponent = component;
                    bestDistance = distance;
                }
            }

            if (selected is not { } uid || selectedComponent == null)
            {
                if (_recipients.Remove(session))
                    RaiseNetworkEvent(new Wh40kBossHudStateEvent(null), session);
                continue;
            }

            _recipients[session] = uid;
            TryGetHealth(uid, out var health, out var maximum);
            var transform = Transform(uid);
            if (transform.MapUid is not { } map)
                continue;
            RaiseNetworkEvent(new Wh40kBossHudStateEvent(new Wh40kBossHudData(GetNetEntity(uid), GetNetEntity(map),
                _transform.GetWorldPosition(transform), selectedComponent.Title, health, maximum,
                _themes.GetValueOrDefault(uid), selectedComponent.Theme?.Params ?? AudioParams.Default,
                selectedComponent.ThemeFadeIn, selectedComponent.ThemeFadeOut)
            {
                Protected = TryComp<Wh40kChaosMarineComponent>(uid, out var marine) && marine.TransitionProtected,
            }), session);
        }

        _removed.Clear();
        foreach (var session in _recipients.Keys)
            if (!_connected.Contains(session))
                _removed.Add(session);
        foreach (var session in _removed)
            _recipients.Remove(session);
    }
}
