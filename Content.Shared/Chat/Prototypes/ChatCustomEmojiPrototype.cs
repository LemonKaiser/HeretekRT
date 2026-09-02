using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Chat.Prototypes;

[Prototype]
public sealed partial class ChatCustomEmojiPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ResPath RsiPath;

    [DataField]
    public string? State;

    [DataField]
    public int Order;

    [DataField]
    public string? DisplayName;

    [DataField]
    public List<string> Keywords = new();

    [DataField]
    public List<string> Aliases = new();

    public ChatEmojiDefinition ToDefinition() => new(
        ID.ToLowerInvariant(),
        string.Empty,
        ChatEmojiCategory.Custom,
        RsiPath,
        string.IsNullOrWhiteSpace(State) ? ID : State,
        DisplayName,
        Keywords);
}
