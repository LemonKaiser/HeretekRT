using System;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client._WH40K.Act1.UI;
using Content.Shared._WH40K.Act1;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Act1;

/// <summary>
/// Draws presentation cues received from the server. It is intentionally non-modal, so the Escape menu keeps its
/// normal behaviour during Act I.
/// </summary>
public sealed class Act1ProloguePresentationController : UIController,
    IOnStateEntered<LobbyState>,
    IOnStateExited<LobbyState>,
    IOnStateEntered<GameplayState>,
    IOnStateExited<GameplayState>,
    IOnSystemChanged<Act1PrologueControlLockSystem>
{
    private static readonly SoundPathSpecifier GlassShatterSound = new("/Audio/_WH40K/Act1/glass-shatter.ogg");

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [UISystemDependency] private readonly AudioSystem _audio = default!;

    private Act1ProloguePresentationControl? _control;
    private Act1PrologueShatterOverlay? _shatterOverlay;
    private TimeSpan? _startedAt;
    private bool _holdingBlack;
    private bool _introPending;

    public void OnSystemLoaded(Act1PrologueControlLockSystem system)
    {
        SubscribeNetworkEvent<Act1ProloguePresentationEvent>(OnPresentation);
        SubscribeNetworkEvent<Act1PrologueFractureEvent>(OnFracture);
    }

    public void OnSystemUnloaded(Act1PrologueControlLockSystem system)
    {
        UnSubscribeNetworkEvent<Act1ProloguePresentationEvent>();
        UnSubscribeNetworkEvent<Act1PrologueFractureEvent>();
    }

    public void OnStateEntered(GameplayState state)
    {
        // The transition is attached directly to the persistent window root, so
        // rebuilding Lobby/Gameplay screens cannot remove it. Reattach only an
        // active transition so a fresh gameplay state never starts black.
        if (_holdingBlack || _introPending || _startedAt != null)
            EnsureActiveControl();
    }

    public void OnStateEntered(LobbyState state)
    {
    }

    public void OnStateExited(LobbyState state)
    {
    }

    public void OnStateExited(GameplayState state)
    {
        _startedAt = null;
        _holdingBlack = false;
        _introPending = false;
        ClearShatter();
        if (_control == null)
            return;

        _control.Orphan();
        _control.Dispose();
        _control = null;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var active = _players.LocalEntity is { } player &&
                     _entities.HasComponent<Act1PrologueComponent>(player);
        if (!active && !_holdingBlack && !_introPending && _startedAt == null)
        {
            if (_control != null)
                _control.Visible = false;
            ClearShatter();
        }

        // Keep the same fade alive across Lobby -> Gameplay. The persistent root
        // and existing timer prevent a flash of the world between both states.
        if (_holdingBlack || _introPending || _startedAt != null)
            EnsureActiveControl();

        if (_control == null)
            return;

        if (_holdingBlack)
        {
            var elapsed = _startedAt is { } startedAt ? _timing.CurTime - startedAt : TimeSpan.Zero;
            _control.SetElapsed(TimeSpan.FromTicks(Math.Min(elapsed.Ticks, Act1ProloguePresentationTiming.FadeToBlackDuration.Ticks)));

            if (_introPending && elapsed >= Act1ProloguePresentationTiming.FadeToBlackDuration && IsWhiteRoomReady())
                StartIntro();
            else if (elapsed >= Act1ProloguePresentationTiming.TransitionTimeout)
                CancelLobbyTransition();
        }
        else if (_startedAt is { } presentationStartedAt)
        {
            var elapsed = _timing.CurTime - presentationStartedAt;
            _control.SetElapsed(elapsed);
            if (elapsed >= _control.Duration)
            {
                _control.Visible = false;
                _startedAt = null;
            }
        }

    }

    private void OnPresentation(Act1ProloguePresentationEvent ev, EntitySessionEventArgs args)
    {
        var active = _players.LocalEntity is { } player &&
                     _entities.HasComponent<Act1PrologueComponent>(player);

        if (ev.Cue == Act1ProloguePresentationCue.FadeToBlack)
        {
            BeginLobbyTransition();
            return;
        }

        if (ev.Cue == Act1ProloguePresentationCue.CancelTransition)
        {
            CancelLobbyTransition();
            return;
        }

        if (ev.Cue == Act1ProloguePresentationCue.Intro)
        {
            // The cue may arrive before the newly spawned body and its prologue
            // component are replicated. Keep it pending instead of dropping it;
            // FrameUpdate waits for IsWhiteRoomReady before revealing the scene.
            _introPending = true;
            BeginLobbyTransition();
            return;
        }

        if (!active && ev.Cue is not Act1ProloguePresentationCue.FadeFromBlack)
            return;

        if (ev.Cue == Act1ProloguePresentationCue.ShatterToBlack)
        {
            ShowFracture(1f, blackout: true);
            return;
        }

        _holdingBlack = false;
        _introPending = false;
        ClearShatter();
        StartCue(ev.Cue);
    }

    /// <summary>
    /// Starts immediately in the lobby. The subsequent server cue does not restart this fade.
    /// </summary>
    public void BeginLobbyTransition()
    {
        if (_holdingBlack)
            return;

        ClearShatter();
        _holdingBlack = true;
        StartCue(Act1ProloguePresentationCue.FadeToBlack);
    }

    private void StartIntro()
    {
        _holdingBlack = false;
        _introPending = false;
        StartCue(Act1ProloguePresentationCue.Intro);
    }

    private void CancelLobbyTransition()
    {
        if (!_holdingBlack)
            return;

        _holdingBlack = false;
        _introPending = false;
        ClearShatter();
        StartCue(Act1ProloguePresentationCue.FadeFromBlack);
    }

    private bool IsWhiteRoomReady()
    {
        return _players.LocalEntity is { } player &&
               _entities.HasComponent<Act1PrologueComponent>(player);
    }

    private void StartCue(Act1ProloguePresentationCue cue)
    {
        EnsureActiveControl();
        _control!.Start(cue);
        _startedAt = _timing.CurTime;
    }

    private void EnsureActiveControl()
    {
        EnsureControl();
        _control!.Visible = true;
    }

    private void EnsureControl()
    {
        _control ??= new Act1ProloguePresentationControl();

        if (_control.Parent != UIManager.RootControl)
        {
            _control.Orphan();
            UIManager.RootControl.AddChild(_control);
            LayoutContainer.SetAnchorPreset(_control, LayoutContainer.LayoutPreset.Wide);
        }

        _control.SetPositionLast();
    }

    private void OnFracture(Act1PrologueFractureEvent ev, EntitySessionEventArgs args)
    {
        if (_players.LocalEntity is not { } player ||
            !_entities.HasComponent<Act1PrologueComponent>(player))
        {
            return;
        }

        ShowFracture(Math.Clamp(ev.Degree, 0f, 1f), ev.Blackout);
    }

    private void ShowFracture(float degree, bool blackout)
    {
        _shatterOverlay ??= new Act1PrologueShatterOverlay();
        if (!_overlays.HasOverlay<Act1PrologueShatterOverlay>())
        {
            _overlays.AddOverlay(_shatterOverlay);
            if (!blackout)
                _audio.PlayGlobal(GlassShatterSound, Filter.Local(), false);
        }

        _shatterOverlay.Fracture = degree;
        _shatterOverlay.Blackout = blackout ? 1f : 0f;
    }

    private void ClearShatter()
    {
        if (_shatterOverlay != null && _overlays.HasOverlay<Act1PrologueShatterOverlay>())
            _overlays.RemoveOverlay(_shatterOverlay);
    }
}
