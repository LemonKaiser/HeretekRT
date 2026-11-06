using Content.Shared.Chat;
using Content.Shared.StatusIcon;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Chat.RichText;

/// <summary>
///     Injects a job icon immediately before a marked sender name. The marker is deliberately required so an
///     anonymous name (for example, an unidentifiable whisper) never exposes the speaker's role.
/// </summary>
public static class ChatJobIconMarkup
{
    public const string TagName = "chatjobicon";
    private const string NameTagOpen = "[Name]";

    /// <summary>
    ///     The icon represents credentials presented during ordinary speech or radio transmission. It must not
    ///     leak into OOC, administrative, ghost, whisper, or emote messages.
    /// </summary>
    public static bool ShouldInjectForChannel(ChatChannel channel)
        => channel is ChatChannel.Local or ChatChannel.Radio;

    public static string Inject(string wrappedMessage, string iconId)
    {
        if (string.IsNullOrWhiteSpace(iconId) ||
            wrappedMessage.Contains($"[{TagName}", StringComparison.Ordinal))
        {
            return wrappedMessage;
        }

        var nameIndex = wrappedMessage.IndexOf(NameTagOpen, StringComparison.Ordinal);
        if (nameIndex < 0)
            return wrappedMessage;

        var iconMarkup = $"[{TagName} icon=\"{iconId}\"/] ";
        return wrappedMessage.Insert(nameIndex, iconMarkup);
    }
}

[UsedImplicitly]
public sealed partial class ChatJobIconTag : IMarkupTagHandler
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;

    public string Name => ChatJobIconMarkup.TagName;

    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        var font = context.Font.Peek();
        if (TryGetIcon(node, out var jobIcon))
        {
            var texture = _entities.System<SpriteSystem>().Frame0(jobIcon.Icon);
            font = new ChatImageFont(font, texture);
        }
        context.Font.Push(font);
    }

    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context) => context.Font.Pop();

    public string TextBefore(MarkupNode node) => TryGetIcon(node, out _) ? ChatImageFont.Glyph : string.Empty;

    private bool TryGetIcon(MarkupNode node, out JobIconPrototype jobIcon)
    {
        jobIcon = default!;
        return node.Attributes.TryGetValue("icon", out var parameter) &&
               parameter.TryGetString(out var iconId) && _prototypes.TryIndex(iconId, out jobIcon!);
    }
}
