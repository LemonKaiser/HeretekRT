using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.Events;

/// <summary>
/// Client-only visual effects produced by a shuttle impact.
/// The server sends coordinates instead of creating transient spark entities.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleImpactEffectsEvent : EntityEventArgs
{
    public List<NetCoordinates> Coordinates;

    public ShuttleImpactEffectsEvent(List<NetCoordinates> coordinates)
    {
        Coordinates = coordinates;
    }
}
