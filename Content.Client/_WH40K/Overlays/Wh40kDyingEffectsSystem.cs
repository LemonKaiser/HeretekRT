using System.Numerics;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Client._WH40K.Overlays;

/// <summary>
/// Drives the fading-out screen effects and subtle camera sway when the local player's body is badly injured or critical.
/// </summary>
public sealed partial class Wh40kDyingEffectsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedContentEyeSystem _eye = default!;

    /// <summary>Damage, as a share of the crit threshold, where the effects start.</summary>
    public const float StartRatio = 0.5f;

    /// <summary>Level reached at the moment of going critical; the rest is covered by the way to death.</summary>
    public const float CritLevel = 0.55f;

    private const float SwayTiles = 0.14f;
    private const float BlackoutLevel = 0.72f;
    private const float BlackoutLength = 1.8f;

    private Wh40kDyingOverlay _overlay = default!;
    private bool _overlayAdded;
    private EntityUid? _lastEntity;
    private EntityUid? _swaying;

    private bool _enabled;
    private bool _reducedMotion;
    private float _shake;

    private float _level;
    private float _time;
    private float _beatPhase;
    private float _nextBlackout;
    private float _blackoutTime = -1f;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new Wh40kDyingOverlay();
        SubscribeLocalEvent<Wh40kDyingSwayComponent, GetEyeOffsetEvent>(OnGetEyeOffset);

        Subs.CVar(_cfg, CCVars.DyingScreenEffects, value => _enabled = value, true);
        Subs.CVar(_cfg, CCVars.ReducedMotion, value => _reducedMotion = value, true);
        Subs.CVar(_cfg, CCVars.ScreenShakeIntensity, value => _shake = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        StopSway();
        SetOverlay(false);
        _overlay.Dispose();
    }

    /// <summary>How far gone a body is, 0 to 1, from its mob state and how deep into each band its damage is.</summary>
    public static float Level(MobState state, float damage, float critThreshold, float deadThreshold)
    {
        switch (state)
        {
            case MobState.Alive when critThreshold > 0f:
                var ratio = damage / critThreshold;
                return Math.Clamp((ratio - StartRatio) / (1f - StartRatio), 0f, 1f) * CritLevel;
            case MobState.Critical:
                var span = deadThreshold - critThreshold;
                var depth = span > 0f ? Math.Clamp((damage - critThreshold) / span, 0f, 1f) : 1f;
                return CritLevel + (1f - CritLevel) * depth;
            default:
                return 0f;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var local = _player.LocalEntity;
        if (local != _lastEntity)
        {
            _lastEntity = local;
            _level = 0f;
            _blackoutTime = -1f;
            _nextBlackout = 0f;
            StopSway();
        }

        if (!_enabled || local == null)
        {
            _level = 0f;
            _blackoutTime = -1f;
            _overlay.Level = 0f;
            _overlay.Beat = 0f;
            _overlay.Blackout = 0f;
            SetOverlay(false);
            StopSway();
            return;
        }

        var target = TargetLevel(local.Value);

        // Ease toward the target so a big hit or a heal never pops the screen, and fade out quickly on death/recovery.
        var easeSpeed = target <= 0f ? 3f : 1.5f;
        _level += (target - _level) * Math.Min(1f, frameTime * easeSpeed);
        if (_level < 0.01f && target <= 0f)
            _level = 0f;

        if (_level <= 0f)
        {
            _blackoutTime = -1f;
            _overlay.Level = 0f;
            _overlay.Beat = 0f;
            _overlay.Blackout = 0f;
            SetOverlay(false);
            StopSway();
            return;
        }

        _time += frameTime;

        // Pulse races as the body fails, then slows right down deep into crit.
        var bpm = _level < CritLevel
            ? 65f + 60f * (_level / CritLevel)
            : 125f - 80f * ((_level - CritLevel) / (1f - CritLevel));
        _beatPhase = (_beatPhase + frameTime * bpm / 60f) % 1f;
        var beat = MathF.Exp(-_beatPhase * 9f) + 0.6f * MathF.Exp(-((_beatPhase + 0.72f) % 1f) * 9f);

        _overlay.Level = _level;
        _overlay.Beat = Math.Clamp(beat, 0f, 1f);
        _overlay.Blackout = UpdateBlackout(frameTime);
        SetOverlay(true);

        UpdateSway(local.Value, beat);
    }

    private float TargetLevel(EntityUid player)
    {
        if (!TryComp(player, out MobStateComponent? mob) ||
            !TryComp(player, out DamageableComponent? damageable) ||
            !TryComp(player, out MobThresholdsComponent? thresholds) ||
            !thresholds.ShowOverlays)
        {
            return 0f;
        }

        if (!_thresholds.TryGetThresholdForState(player, MobState.Dead, out var dead, thresholds))
            return 0f;

        var crit = _thresholds.TryGetThresholdForState(player, MobState.Critical, out var found, thresholds)
            ? found.Value
            : dead.Value;

        return Level(mob.CurrentState, damageable.TotalDamage.Float(), crit.Float(), dead.Value.Float());
    }

    /// <summary>Eyes drifting shut for a moment, at random, once far enough gone.</summary>
    private float UpdateBlackout(float frameTime)
    {
        if (_level < BlackoutLevel)
        {
            _blackoutTime = -1f;
            _nextBlackout = Math.Max(_nextBlackout, _time + 4f);
            return 0f;
        }

        if (_blackoutTime < 0f)
        {
            if (_time < _nextBlackout)
                return 0f;

            _blackoutTime = 0f;
        }

        _blackoutTime += frameTime;
        if (_blackoutTime >= BlackoutLength)
        {
            _blackoutTime = -1f;
            // More often the closer to the end: every 5 to 11 seconds at worst.
            _nextBlackout = _time + _random.NextFloat(5f, 11f) * (1.6f - _level * 0.6f);
            return 0f;
        }

        var depth = 0.55f + 0.4f * ((_level - BlackoutLevel) / (1f - BlackoutLevel));
        return MathF.Sin(_blackoutTime / BlackoutLength * MathF.PI) * depth;
    }

    private void UpdateSway(EntityUid player, float beat)
    {
        if (_reducedMotion || _shake <= 0f || !TryComp(player, out EyeComponent? eye))
        {
            StopSway();
            return;
        }

        if (_swaying != player)
        {
            StopSway();
            _swaying = player;
        }

        var sway = EnsureComp<Wh40kDyingSwayComponent>(player);
        var amplitude = _level * _level * SwayTiles * _shake;
        sway.Offset = new Vector2(
            MathF.Sin(_time * 0.9f) + 0.5f * MathF.Sin(_time * 2.3f + 1f),
            MathF.Cos(_time * 0.7f) + 0.5f * MathF.Sin(_time * 1.9f)) * amplitude;
        // A small nod on each heartbeat.
        sway.Offset += new Vector2(0f, -beat * 0.02f * _level * _shake);
        _eye.UpdateEyeOffset((player, eye));
    }

    private void StopSway()
    {
        if (_swaying is not { } old)
            return;

        _swaying = null;
        if (TerminatingOrDeleted(old))
            return;

        RemComp<Wh40kDyingSwayComponent>(old);
        if (TryComp(old, out EyeComponent? eye))
            _eye.UpdateEyeOffset((old, eye));
    }

    private void OnGetEyeOffset(Entity<Wh40kDyingSwayComponent> ent, ref GetEyeOffsetEvent args)
    {
        args.Offset += ent.Comp.Offset;
    }

    private void SetOverlay(bool on)
    {
        if (on == _overlayAdded)
            return;

        _overlayAdded = on;
        if (on)
            _overlays.AddOverlay(_overlay);
        else
            _overlays.RemoveOverlay(_overlay);
    }
}

/// <summary>Client-only: the camera drift the dying effects add to the local player's eye.</summary>
[RegisterComponent]
public sealed partial class Wh40kDyingSwayComponent : Component
{
    [ViewVariables]
    public Vector2 Offset;
}
