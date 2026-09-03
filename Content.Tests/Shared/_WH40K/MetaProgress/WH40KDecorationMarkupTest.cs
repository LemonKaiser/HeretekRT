using Content.Shared._WH40K.MetaProgress;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests.Shared._WH40K.MetaProgress;

[TestFixture]
public sealed class WH40KDecorationMarkupTest
{
    [TestCase("false", WH40KDecorationAccessMode.Disabled)]
    [TestCase("admin", WH40KDecorationAccessMode.Admins)]
    [TestCase("all", WH40KDecorationAccessMode.All)]
    [TestCase("unknown", WH40KDecorationAccessMode.Disabled)]
    [TestCase(" ALL ", WH40KDecorationAccessMode.All)]
    public void AccessModeParsingIsFailClosed(string value, WH40KDecorationAccessMode expected)
    {
        Assert.That(WH40KDecorationAccessPolicy.ParseMode(value), Is.EqualTo(expected));
    }

    [Test]
    public void MarkupEscapesRichTextParameterCharacters()
    {
        const string text = "Имя [с \\ обратным слешем], \"кавычками\" и }\r\nновой строкой";
        var markup = WH40KDecorationMarkup.BuildGradientMarkup(
            text,
            ["#112233", "#445566"],
            string.Empty,
            animated: true,
            durationMs: 3000,
            auraColorHex: string.Empty,
            auraRadius: 0,
            auraAlphaPercent: 0);

        Assert.That(markup, Does.Contain("\\["));
        Assert.That(markup, Does.Contain("\\\""));
        Assert.That(markup, Does.Not.Contain("\r"));
        Assert.That(markup, Does.Not.Contain("\n"));
        Assert.That(FormattedMessage.ValidMarkup(markup), Is.True, markup);
    }

    [Test]
    public void AnimatedGradientUsesTransparentLayoutTextAndOverlay()
    {
        var markup = WH40KDecorationMarkup.BuildGradientMarkup(
            "Длинное сообщение для обычного переноса строк",
            ["#112233", "#445566"],
            string.Empty,
            animated: true,
            durationMs: 3000,
            auraColorHex: string.Empty,
            auraRadius: 0,
            auraAlphaPercent: 0);

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.StartWith("[wh40kgradient text=\""));
            Assert.That(markup, Does.Contain("overlay=1]"));
            Assert.That(markup, Does.EndWith("[/wh40kgradient]"));
            Assert.That(FormattedMessage.ValidMarkup(markup), Is.True, markup);
        });
    }

    [Test]
    public void PaletteIsCanonicalAndBounded()
    {
        var palette = WH40KDecorationMarkup.BuildPalette(
            ["red", "#112233", "invalid", "blue", "green", "white", "black", "yellow", "purple", "orange"],
            "#abcdef");

        Assert.That(palette, Has.Count.EqualTo(WH40KDecorationMarkup.MaxPaletteColors));
        Assert.That(palette[0], Is.EqualTo("#FF0000FF"));
        Assert.That(palette, Does.Not.Contain("#ABCDEFFF"));
    }

    [TestCase("fish-swim", "fish")]
    [TestCase("discord-flip", "flip")]
    [TestCase("noise", "noise-dissolve")]
    [TestCase("bad-effect", "")]
    public void TitleEffectNormalizationIsCanonical(string source, string expected)
    {
        Assert.That(WH40KDecorationMarkup.NormalizeTitleEffect(source), Is.EqualTo(expected));
    }

    [Test]
    public void OrdinaryTitleUsesStockRichText()
    {
        var markup = WH40KDecorationMarkup.BuildTitleMarkup(
            "(Обычный титул) Player",
            [],
            string.Empty,
            animated: false,
            gradientDurationMs: 3500,
            effect: string.Empty,
            revealMs: 900,
            holdMs: 10000,
            dissolveMs: 900,
            outlineColorHex: string.Empty,
            outlineWidth: 0,
            outlineAlphaPercent: 0);

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.StartWith("[color="));
            Assert.That(markup, Does.Not.Contain("wh40ktitlefx"));
            Assert.That(FormattedMessage.ValidMarkup(markup), Is.True, markup);
        });
    }

    [TestCase("binary")]
    [TestCase("scan")]
    [TestCase("fish")]
    [TestCase("scramble-decode")]
    [TestCase("typewriter-cursor")]
    [TestCase("wave")]
    [TestCase("glitch-slice")]
    [TestCase("noise-dissolve")]
    [TestCase("scanline")]
    [TestCase("flip")]
    public void SupportedTitleEffectsBuildValidMarkup(string effect)
    {
        var markup = WH40KDecorationMarkup.BuildTitleMarkup(
            "(Проверка) Player",
            ["#112233", "#445566"],
            string.Empty,
            animated: true,
            gradientDurationMs: 3500,
            effect: effect,
            revealMs: 900,
            holdMs: 10000,
            dissolveMs: 900,
            outlineColorHex: string.Empty,
            outlineWidth: 0,
            outlineAlphaPercent: 0);

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("wh40ktitlefx"));
            Assert.That(markup, Does.Contain($"effect=\"{effect}\""));
            Assert.That(FormattedMessage.ValidMarkup(markup), Is.True, markup);
        });
    }

    [Test]
    public void TitleFallbackStyleKeepsAnimationDurationAndAura()
    {
        var markup = WH40KDecorationMarkup.BuildTitleMarkup(
            "(Титул) Player: сообщение",
            [],
            string.Empty,
            animated: false,
            gradientDurationMs: 3500,
            effect: "glitch-slice",
            revealMs: 900,
            holdMs: 10000,
            dissolveMs: 900,
            outlineColorHex: string.Empty,
            outlineWidth: 0,
            outlineAlphaPercent: 0,
            fallbackGradientColors: ["#112233", "#445566"],
            fallbackSolidColor: string.Empty,
            fallbackAnimated: true,
            fallbackGradientDurationMs: 1900,
            fallbackAuraColorHex: "#ABCDEF",
            fallbackAuraRadius: 2,
            fallbackAuraAlphaPercent: 70);

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("animated=1"));
            Assert.That(markup, Does.Contain("duration=1900"));
            Assert.That(markup, Does.Contain("aura=1"));
            Assert.That(markup, Does.Contain("auraradius=2"));
            Assert.That(FormattedMessage.ValidMarkup(markup), Is.True, markup);
        });
    }

    [Test]
    public void SelectionMutationChangesOnlyRequestedCategory()
    {
        var selection = new WH40KDecorationSelection("ghost", "title", "color");
        var next = selection.WithSelection(WH40KMetaDecorationCategory.OocTitles, "new-title");

        Assert.Multiple(() =>
        {
            Assert.That(next.SelectedGhostSkinId, Is.EqualTo("ghost"));
            Assert.That(next.SelectedOocTitleId, Is.EqualTo("new-title"));
            Assert.That(next.SelectedOocNameColorId, Is.EqualTo("color"));
            Assert.That(next, Is.Not.EqualTo(selection));
            Assert.That(next, Is.EqualTo(new WH40KDecorationSelection("ghost", "new-title", "color")));
        });
    }
}
