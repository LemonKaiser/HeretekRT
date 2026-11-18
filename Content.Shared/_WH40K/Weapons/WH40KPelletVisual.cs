using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Weapons;

[Serializable, NetSerializable]
public enum WH40KPelletVisual : byte
{
    Default,
    Plain,
    Tracer,
}
