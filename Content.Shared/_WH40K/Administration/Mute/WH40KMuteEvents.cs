using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Administration.Mute;

[Serializable, NetSerializable]
public sealed class WH40KMuteRequestStateEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class WH40KMuteStateEvent(WH40KMuteSnapshot snapshot) : EntityEventArgs
{
    public WH40KMuteSnapshot Snapshot { get; } = snapshot;
}
