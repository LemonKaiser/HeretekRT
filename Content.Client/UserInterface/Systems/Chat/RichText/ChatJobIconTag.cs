using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Chat;
using Content.Shared.StatusIcon;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
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
    private const string OutputLineHeightSpacer = "[font size=3]\n[/font]";

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

    /// <summary>
    ///     OutputPanel measures the first rich-text line before it accounts for an inline control's height.
    ///     This invisible short line reserves the missing space for the full-size icon without modifying the
    ///     message used by speech bubbles.
    /// </summary>
    public static string ReserveOutputLineHeight(string wrappedMessage)
    {
        if (!wrappedMessage.Contains($"[{TagName}", StringComparison.Ordinal) ||
            wrappedMessage.EndsWith(OutputLineHeightSpacer, StringComparison.Ordinal))
        {
            return wrappedMessage;
        }

        return wrappedMessage + OutputLineHeightSpacer;
    }
}

[UsedImplicitly]
public sealed partial class ChatJobIconTag : IMarkupTagHandler
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Vector2 IconSize = new(20f, 20f);

    public string Name => ChatJobIconMarkup.TagName;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!node.Attributes.TryGetValue("icon", out var iconParameter) ||
            !iconParameter.TryGetString(out var iconId) ||
            !_prototypes.TryIndex<JobIconPrototype>(iconId, out var jobIcon))
        {
            return false;
        }

        control = new TextureRect
        {
            Texture = _entities.System<SpriteSystem>().Frame0(jobIcon.Icon),
            TextureScale = new Vector2(2.5f, 2.5f),
            Stretch = TextureRect.StretchMode.KeepCentered,
            VerticalAlignment = Control.VAlignment.Center,
            MinSize = IconSize,
            Margin = new Thickness(0f, 3f, 2f, 0f),
        };
        return true;
    }
}
