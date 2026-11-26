using Lidgren.Network;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Chat;

[CVarDefs]
public static class Wh40kChatTranslationCVars
{
    public static readonly CVarDef<bool> Enabled = CVarDef.Create("wh40k.chat_translation", true, CVar.SERVERONLY);

    public static readonly CVarDef<string> GeminiApiKey = CVarDef.Create(
        "wh40k.gemini_api_key", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);
}

/// <summary>A supplement to a chat packet already delivered on the reliable command channel.</summary>
public sealed class MsgChatTranslation : NetMessage
{
    public const int MaxTextLength = 3000;

    public override MsgGroups MsgGroup => MsgGroups.Command;
    public ulong Id;
    public string Culture = string.Empty;
    public string Text = string.Empty;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Id = buffer.ReadUInt64();
        Culture = buffer.ReadString();
        Text = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Id);
        buffer.Write(Culture);
        buffer.Write(Text);
    }
}
