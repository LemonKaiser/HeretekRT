using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Activities;

[Serializable, NetSerializable]
public sealed partial class KoronusActivityWorkDoAfterEvent : SimpleDoAfterEvent;
