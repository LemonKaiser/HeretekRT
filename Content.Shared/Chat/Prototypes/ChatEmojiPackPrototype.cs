using Robust.Shared.Prototypes;

namespace Content.Shared.Chat.Prototypes;

/// <summary>
/// A compact content-defined pack of built-in Unicode emoji. Entries use <c>alias=HEX-HEX</c> format.
/// </summary>
[Prototype]
public sealed partial class ChatEmojiPackPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ChatEmojiCategory Category;

    [DataField]
    public int Order;

    [DataField(required: true)]
    public string Definitions = string.Empty;
}
