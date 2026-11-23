using Content.Shared.CCVar;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Client._WH40K.Audio;

/// <summary>
/// Plays a looping heartbeat while the local player's own body is in critical condition.
/// </summary>
[UsedImplicitly]
public sealed partial class Wh40kCritHeartbeatSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private const string HeartbeatSound = "/Audio/_WH40K/Effects/heartbeat_loop.ogg";

    private EntityUid? _stream;
    private bool _cvarEnabled = true;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);

        Subs.CVar(_cfg, CCVars.CritHeartbeatEnabled, OnCVarChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Stop();
    }

    private void OnCVarChanged(bool value)
    {
        _cvarEnabled = value;
        Refresh();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        Refresh();
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        Stop();
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.Target != _player.LocalEntity)
            return;

        Refresh();
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        if (args.Entity.Owner != _player.LocalEntity)
            return;

        Stop();
    }

    /// <summary>
    /// Reconciles every frame in case a state change arrives via prediction on a non-first-time-predicted tick.
    /// </summary>
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        Refresh();
    }

    private void Refresh()
    {
        if (_cvarEnabled &&
            _player.LocalEntity is { } local &&
            TryComp<MobStateComponent>(local, out var mobState) &&
            mobState.CurrentState == MobState.Critical)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        if (_stream is { } existing && !Deleted(existing))
            return;

        var played = _audio.PlayGlobal(
            new ResolvedPathSpecifier(HeartbeatSound),
            Filter.Local(),
            false,
            AudioParams.Default.WithLoop(true).WithVolume(-4f));
        _stream = played?.Entity;
    }

    private void Stop()
    {
        if (_stream is not { } stream)
            return;

        // Delete directly: SharedAudioSystem.Stop is a no-op on ticks that are not first-time-predicted.
        _stream = null;
        if (!Deleted(stream))
            QueueDel(stream);
    }
}
