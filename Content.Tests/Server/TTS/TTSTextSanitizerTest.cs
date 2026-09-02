using Content.Server.TTS;
using NUnit.Framework;

namespace Content.Tests.Server.TTS;

[TestFixture]
public sealed class TTSTextSanitizerTest
{
    [Test]
    public void SanitizerStripsTagsAndPreservesKnownAbbreviations()
    {
        var result = TTSSystem.Sanitize("[color=red]ГП[/color], GPS и ID");

        Assert.That(result, Is.EqualTo("Гэ Пэ, Джи Пи Эс и Ай Ди"));
    }

    [Test]
    public void SanitizerExpandsNumbersAndDecimals()
    {
        var result = TTSSystem.Sanitize("  12,5  ");

        Assert.That(result, Is.EqualTo("двенадцать целых пять"));
    }

    [Test]
    public void SanitizerTransliteratesUnknownLatinWords()
    {
        var result = TTSSystem.Sanitize("test");

        Assert.That(result, Is.EqualTo("тест"));
    }
}
