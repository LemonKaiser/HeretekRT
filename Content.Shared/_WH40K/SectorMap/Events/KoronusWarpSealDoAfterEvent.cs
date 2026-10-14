using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SectorMap.Events;

[Serializable, NetSerializable]
public sealed partial class KoronusWarpSealDoAfterEvent : SimpleDoAfterEvent;
