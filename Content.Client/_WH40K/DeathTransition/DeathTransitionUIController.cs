using System;
using Content.Client.Gameplay;
using Content.Client._WH40K.DeathTransition.UI;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Content.Shared._WH40K.DeathTransition;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.DeathTransition;

/// <summary>
/// Coordinates the presentational part of an already server-authorized death transition.
/// </summary>
public sealed class DeathTransitionUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    private static readonly SoundPathSpecifier DeathStartSound = new("/Audio/_WH40K/DeathTransition/deathstart.wav");
    private static readonly SoundPathSpecifier DeathNameSound = new("/Audio/_WH40K/DeathTransition/deathname.wav");

    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;
    [UISystemDependency] private readonly AudioSystem _audio = default!;

    private DeathTransitionOverlay? _overlay;
    private DeathTransitionControl? _control;
    private TimeSpan? _startedAt;
    private TimeSpan _duration;
    private int _transitionId;
    private EntityUid? _deathStartStream;
    private EntityUid? _deathNameStream;
    private bool _deathNameStarted;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<DeathTransitionStartEvent>(OnDeathTransitionStarted);
        SubscribeNetworkEvent<DeathTransitionCancelledEvent>(OnDeathTransitionCancelled);
    }

    public void OnStateEntered(GameplayState state)
    {
        EnsurePresentationObjects();
    }

    public void OnStateExited(GameplayState state)
    {
        ClearPresentation();

        if (_control != null)
        {
            _control.Orphan();
            _control.Dispose();
            _control = null;
        }

        _overlay = null;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_startedAt == null || _duration <= TimeSpan.Zero || _overlay == null || _control == null)
            return;

        var elapsed = _timing.CurTime - _startedAt.Value;
        _overlay.Progress = Math.Clamp((float) (elapsed / DeathTransitionTiming.ScreenFadeDuration), 0f, 1f);
        _control.SetElapsed(elapsed);

        if (!_deathNameStarted && elapsed >= DeathTransitionTiming.ScreenFadeDuration)
        {
            _deathNameStarted = true;
            _deathNameStream = _audio.PlayGlobal(DeathNameSound, Filter.Local(), false)?.Entity;
        }
    }

    private void OnDeathTransitionStarted(DeathTransitionStartEvent ev, EntitySessionEventArgs args)
    {
        if (_startedAt != null && ev.TransitionId <= _transitionId)
            return;

        // The event can arrive after a content hot reload, when this controller did not
        // receive the already-past GameplayState entry notification. Do not play sound only.
        EnsurePresentationObjects();
        _transitionId = ev.TransitionId;
        _startedAt = _timing.CurTime;
        _duration = ev.Duration;
        _deathNameStarted = false;
        StopDeathSounds();
        _deathStartStream = _audio.PlayGlobal(DeathStartSound, Filter.Local(), false)?.Entity;
        _control?.SetElapsed(TimeSpan.Zero);
        EnsurePresentation();
    }

    private void OnDeathTransitionCancelled(DeathTransitionCancelledEvent ev, EntitySessionEventArgs args)
    {
        if (_startedAt == null || ev.TransitionId != _transitionId)
            return;

        ClearPresentation();
    }

    private void EnsurePresentationObjects()
    {
        _overlay ??= new DeathTransitionOverlay();
        _control ??= new DeathTransitionControl();
    }

    private void EnsurePresentation()
    {
        if (_overlay == null || _control == null)
            return;

        if (!_overlays.HasOverlay<DeathTransitionOverlay>())
            _overlays.AddOverlay(_overlay);

        if (_control.Parent != UIManager.PopupRoot)
        {
            _control.Orphan();
            UIManager.PopupRoot.AddChild(_control);
            LayoutContainer.SetAnchorPreset(_control, LayoutContainer.LayoutPreset.Wide);
        }

        _control.SetPositionLast();
        _control.Visible = true;
        _control.GrabKeyboardFocus();
    }

    private void ClearPresentation()
    {
        _startedAt = null;
        _duration = TimeSpan.Zero;
        _deathNameStarted = false;
        StopDeathSounds();

        if (_overlay != null && _overlays.HasOverlay<DeathTransitionOverlay>())
            _overlays.RemoveOverlay(_overlay);

        if (_control != null)
        {
            _control.ReleaseKeyboardFocus();
            _control.Visible = false;
        }
    }

    private void StopDeathSounds()
    {
        if (_deathStartStream is { } deathStartStream)
            _audio.Stop(deathStartStream);

        if (_deathNameStream is { } deathNameStream)
            _audio.Stop(deathNameStream);

        _deathStartStream = null;
        _deathNameStream = null;
    }
}
