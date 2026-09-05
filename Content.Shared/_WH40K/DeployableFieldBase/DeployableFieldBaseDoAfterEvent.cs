using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.DeployableFieldBase;

[Serializable, NetSerializable]
public sealed partial class DeployableFieldBaseDoAfterEvent : SimpleDoAfterEvent;
