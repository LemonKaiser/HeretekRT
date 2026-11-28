using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Construction;

/// <summary>
/// Client requests the server to craft an item construction recipe repeatedly.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kCraftRepeatRequestEvent : EntityEventArgs
{
    public const int MaxCount = 100;

    public readonly string PrototypeName;
    public readonly int Count;

    public Wh40kCraftRepeatRequestEvent(string prototypeName, int count)
    {
        PrototypeName = prototypeName;
        Count = count;
    }
}
