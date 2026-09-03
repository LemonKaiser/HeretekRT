using System;
using Content.Server._WH40K.MetaProgress;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Player;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Managers;

internal sealed partial class ChatManager
{
    /// <summary>
    ///     Формирует безопасную разметку для выбранного имени, титула и текста OOC.
    ///     Цвет применяется также к сообщению, чтобы украшение было видно не только в имени.
    /// </summary>
    private bool TryBuildDecoratedOocMessage(ICommonSession player, string message, out string wrappedMessage)
    {
        return TryBuildDecoratedChatMessage(
            _entityManager.System<WH40KDecorationSystem>(),
            player,
            player.Name,
            message,
            "chat-manager-send-ooc-decoration-markup-wrap-message",
            "chat-manager-send-ooc-decoration-full-line",
            out wrappedMessage);
    }

    /// <summary>
    ///     Формирует разметку выбранных украшений для OOC-подобного сообщения.
    /// </summary>
    internal static bool TryBuildDecoratedChatMessage(
        WH40KDecorationSystem decorations,
        ICommonSession player,
        string name,
        string message,
        string wrapperKey,
        string fullLineKey,
        out string wrappedMessage)
    {
        wrappedMessage = string.Empty;

        WH40KMetaDecorationPrototype? color;
        WH40KMetaDecorationPrototype? title;
        try
        {
            decorations.TryGetSelectedDecoration(player.UserId, WH40KMetaDecorationCategory.OocNameColors, out color);
            decorations.TryGetSelectedDecoration(player.UserId, WH40KMetaDecorationCategory.OocTitles, out title);
        }
        catch (Exception exception)
        {
            Logger.GetSawmill("wh40k.decorations").Warning($"Failed to resolve WH40K chat decorations for {player.Name}: {exception}");
            return false;
        }

        if (decorations.ShouldApplyFullLineEffect(player))
        {
            var titlePrefix = BuildTitlePrefix(title);
            var fullLine = Loc.GetString(
                fullLineKey,
                ("titlePrefix", string.IsNullOrEmpty(titlePrefix) ? string.Empty : $"{titlePrefix} "),
                ("playerName", name),
                ("message", message));
            var fullLineMarkup = BuildFullLineMarkup(title, color, fullLine);
            if (!string.IsNullOrWhiteSpace(fullLineMarkup))
            {
                wrappedMessage = fullLineMarkup;
                return true;
            }
        }

        // The colon is part of the decorated player name, so a selected colour also covers it.
        var nameMarkup = BuildGradientMarkup(color, $"{name}:");
        var messageMarkup = BuildGradientMarkup(color, message);
        var titleMarkup = BuildTitleMarkup(title, color);
        if (string.IsNullOrWhiteSpace(nameMarkup) && string.IsNullOrWhiteSpace(titleMarkup))
            return false;

        var displayName = string.IsNullOrWhiteSpace(titleMarkup)
            ? string.Empty
            : $"{titleMarkup} ";
        displayName += string.IsNullOrWhiteSpace(nameMarkup)
            ? FormattedMessage.EscapeText($"{name}:")
            : nameMarkup;

        wrappedMessage = Loc.GetString(
            wrapperKey,
            ("playerNameMarkup", (object)displayName),
            ("message", string.IsNullOrWhiteSpace(messageMarkup)
                ? FormattedMessage.EscapeText(message)
                : messageMarkup));
        return true;
    }

    private static string BuildGradientMarkup(WH40KMetaDecorationPrototype? decoration, string text)
    {
        return decoration == null
            ? string.Empty
            : WH40KDecorationMarkup.BuildGradientMarkup(
                text,
                decoration.OocGradientColors,
                decoration.OocColorHex,
                decoration.OocGradientAnimated,
                decoration.OocGradientDurationMs,
                decoration.OocAuraHex,
                decoration.OocAuraRadius,
                decoration.OocAuraAlphaPercent);
    }

    private static string BuildTitleMarkup(
        WH40KMetaDecorationPrototype? title,
        WH40KMetaDecorationPrototype? color)
    {
        if (title == null || title.SuppressTitlePrefix)
            return string.Empty;

        var text = BuildTitlePrefix(title);
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return BuildTitleEffectMarkup(title, color, text);
    }

    private static string BuildFullLineMarkup(
        WH40KMetaDecorationPrototype? title,
        WH40KMetaDecorationPrototype? color,
        string text)
    {
        if (title != null && !title.SuppressTitlePrefix)
            return BuildTitleEffectMarkup(title, color, text);

        return BuildGradientMarkup(color, text);
    }

    private static string BuildTitleEffectMarkup(
        WH40KMetaDecorationPrototype title,
        WH40KMetaDecorationPrototype? color,
        string text)
    {
        // A title owns its effect and outline.  If it has no visual palette of its own,
        // the selected name-color owns the complete visual style: palette, animation,
        // duration and aura.  Previously only the fallback palette was inherited, which
        // made full-line mode freeze otherwise animated name colors.
        var visualStyle = HasOwnVisualStyle(title) ? title : color ?? title;
        return WH40KDecorationMarkup.BuildTitleMarkup(
            text,
            visualStyle.OocGradientColors,
            visualStyle.OocColorHex,
            visualStyle.OocGradientAnimated,
            visualStyle.OocGradientDurationMs,
            title.OocTitleEffect,
            title.OocTitleEffectRevealMs,
            title.OocTitleEffectHoldMs,
            title.OocTitleEffectDissolveMs,
            title.OocTitleOutlineHex,
            title.OocTitleOutlineWidth,
            title.OocTitleOutlineAlphaPercent,
            auraColorHex: visualStyle.OocAuraHex,
            auraRadius: visualStyle.OocAuraRadius,
            auraAlphaPercent: visualStyle.OocAuraAlphaPercent);
    }

    private static bool HasOwnVisualStyle(WH40KMetaDecorationPrototype decoration)
    {
        if (WH40KDecorationMarkup.BuildPalette(decoration.OocGradientColors, decoration.OocColorHex).Count > 0)
            return true;

        return decoration.OocAuraRadius > 0 && decoration.OocAuraAlphaPercent > 0 &&
               WH40KDecorationMarkup.TryResolveColor(decoration.OocAuraHex, out _);
    }

    private static string BuildTitlePrefix(WH40KMetaDecorationPrototype? title)
    {
        if (title == null || title.SuppressTitlePrefix)
            return string.Empty;

        var key = string.IsNullOrWhiteSpace(title.PreviewKey) ? title.TitleKey : title.PreviewKey;
        var text = Loc.GetString(key);
        return string.IsNullOrWhiteSpace(text) ? string.Empty : $"({text})";
    }
}
