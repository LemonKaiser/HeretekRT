using System;
using System.IO;
using System.Threading;
using Content.Client.Resources.Gif;
using NUnit.Framework;

namespace Content.Tests.Client.Resources.Gif;

[TestFixture]
public sealed class GifDecoderTests
{
    private static readonly byte[] SinglePixelGif =
        Convert.FromBase64String("R0lGODlhAQABAIABAAAAAP///yH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==");

    [Test]
    public void DecodeEmptyInputReturnsEmptyAnimation()
    {
        var decoded = GifDecoder.Decode(ReadOnlyMemory<byte>.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Width, Is.EqualTo(0));
            Assert.That(decoded.Height, Is.EqualTo(0));
            Assert.That(decoded.Frames, Is.Empty);
        });
    }

    [Test]
    public void DecodeSinglePixelGifReturnsRgbaFrame()
    {
        var decoded = GifDecoder.Decode(SinglePixelGif);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Width, Is.EqualTo(1));
            Assert.That(decoded.Height, Is.EqualTo(1));
            Assert.That(decoded.Frames, Has.Length.EqualTo(1));
            Assert.That(decoded.Frames[0].Pixels, Has.Length.EqualTo(4));
            Assert.That(decoded.Frames[0].DelaySeconds, Is.GreaterThan(0f));
        });
    }

    [Test]
    public void DecodeFirstFrameMatchesFullDecodeFirstFrame()
    {
        var full = GifDecoder.Decode(SinglePixelGif);
        var firstFrame = GifDecoder.DecodeFirstFrame(SinglePixelGif);

        Assert.Multiple(() =>
        {
            Assert.That(firstFrame.Width, Is.EqualTo(full.Width));
            Assert.That(firstFrame.Height, Is.EqualTo(full.Height));
            Assert.That(firstFrame.Frames, Has.Length.EqualTo(1));
            Assert.That(firstFrame.Frames[0].DelaySeconds, Is.EqualTo(full.Frames[0].DelaySeconds));
            Assert.That(firstFrame.Frames[0].Pixels, Is.EqualTo(full.Frames[0].Pixels));
        });
    }

    [Test]
    public void DecodeStreamMatchesMemoryDecode()
    {
        using var stream = new MemoryStream(SinglePixelGif, writable: false);

        var fromMemory = GifDecoder.Decode(SinglePixelGif);
        var fromStream = GifDecoder.Decode(stream, GifDecoder.DecodeOptions.Default);

        Assert.Multiple(() =>
        {
            Assert.That(fromStream.Width, Is.EqualTo(fromMemory.Width));
            Assert.That(fromStream.Height, Is.EqualTo(fromMemory.Height));
            Assert.That(fromStream.Frames.Length, Is.EqualTo(fromMemory.Frames.Length));
            Assert.That(fromStream.Frames[0].Pixels, Is.EqualTo(fromMemory.Frames[0].Pixels));
        });
    }

    [Test]
    public void DecodeInvalidSignatureThrows()
    {
        var garbage = new byte[] { 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };

        Assert.Throws<InvalidDataException>(() => GifDecoder.Decode(garbage));
    }

    [Test]
    public void DecodeWithCancellationThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => GifDecoder.Decode(SinglePixelGif, cts.Token));
    }
}
