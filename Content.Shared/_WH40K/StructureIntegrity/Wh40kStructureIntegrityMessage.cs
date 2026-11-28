using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.StructureIntegrity;

/// <summary>
/// Sent to an examiner so the examine tooltip can render a styled WH40K structural integrity bar.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kStructureIntegrityMessage : EntityEventArgs
{
    public readonly NetEntity Target;
    public readonly int Current;
    public readonly int Max;

    public Wh40kStructureIntegrityMessage(NetEntity target, int current, int max)
    {
        Target = target;
        Current = current;
        Max = max;
    }
}
