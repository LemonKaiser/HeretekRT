using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration.BanList;

public static class BanListEuiMessages
{
    [Serializable, NetSerializable]
    public sealed class SetMuteHistoryOffset(int offset) : EuiMessageBase
    {
        public int Offset { get; } = offset;
    }
}
