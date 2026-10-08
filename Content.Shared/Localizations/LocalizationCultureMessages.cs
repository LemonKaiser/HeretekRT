using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Localizations;

/// <summary>
/// Carries the client's selected interface culture and the server's canonical acknowledgement.
/// </summary>
public sealed class MsgSetClientCulture : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public string CultureName { get; set; } = string.Empty;

    public bool Accepted { get; set; }

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        CultureName = buffer.ReadString();
        Accepted = buffer.ReadBoolean();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(CultureName);
        buffer.Write(Accepted);
    }
}
