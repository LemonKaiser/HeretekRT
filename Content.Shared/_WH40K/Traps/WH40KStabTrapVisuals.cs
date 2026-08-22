using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Traps;

/// <summary>
/// Visual states of a retractable floor spike trap.
/// </summary>
[Serializable, NetSerializable]
public enum WH40KStabTrapVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum WH40KStabTrapVisualState : byte
{
    Idle,
    Activating,
}
