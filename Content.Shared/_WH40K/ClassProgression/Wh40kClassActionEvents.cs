using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ClassProgression;

/// <summary>
/// Shared wire events used by class-granted Action entities. The server resolves the concrete effect from
/// the Action entity marker; clients cannot supply an effect id or runtime parameters.
/// </summary>
public sealed partial class Wh40kClassInstantActionEvent : InstantActionEvent;

public sealed partial class Wh40kClassEntityTargetActionEvent : EntityTargetActionEvent;

public sealed partial class Wh40kClassWorldTargetActionEvent : WorldTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class Wh40kClassDeviceDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class Wh40kClassFinisherDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class Wh40kClassVerdictDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Sent only to the owning player. It intentionally contains no server runtime type and creates no replicated mark
/// on the target, so the personal priority marker cannot reveal entities through walls to other clients.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kClassTargetMarkVisualEvent(NetEntity target, float duration, bool clear = false) : EntityEventArgs
{
    public NetEntity Target { get; } = target;
    public float Duration { get; } = duration;
    public bool Clear { get; } = clear;
}
