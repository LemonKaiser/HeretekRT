using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.DeathTransition;

/// <summary>
/// Shared schedule for the server-authoritative death transition and its client presentation.
/// </summary>
public static class DeathTransitionTiming
{
    public static readonly TimeSpan ScreenFadeDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan TitleRevealDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan TitleHoldDuration = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan TotalDuration = ScreenFadeDuration + TitleRevealDuration + TitleHoldDuration;
}

/// <summary>
/// Tells a client whether its ordinary observe button may be used.
/// Staff access is evaluated separately from this flag.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostPermissionStatusEvent(bool canObserve) : EntityEventArgs
{
    public bool CanObserve { get; } = canObserve;
}

/// <summary>
/// Starts the non-interactive death presentation before the server returns a player to the lobby.
/// </summary>
[Serializable, NetSerializable]
public sealed class DeathTransitionStartEvent(int transitionId, TimeSpan duration) : EntityEventArgs
{
    public int TransitionId { get; } = transitionId;
    public TimeSpan Duration { get; } = duration;
}

/// <summary>
/// Cancels a pending death transition when the player has been revived before its return timer expires.
/// </summary>
[Serializable, NetSerializable]
public sealed class DeathTransitionCancelledEvent(int transitionId) : EntityEventArgs
{
    public int TransitionId { get; } = transitionId;
}

/// <summary>
/// Sent by the dead player's standard R keybind when they deliberately give up waiting for revival.
/// The server validates both the body state and the sender before starting a death transition.
/// </summary>
[Serializable, NetSerializable]
public sealed class DeathSurrenderEvent : EntityEventArgs;
