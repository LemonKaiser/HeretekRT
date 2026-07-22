using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WH40K.Administration.ScreenCheck;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Network;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WH40K.Administration.ScreenCheck;

public sealed partial class ScreenCheckClientManager
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private ILogManager _logs = default!;
    [Dependency] private IClientNetManager _net = default!;

    private ISawmill _sawmill = default!;
    private int _captureInProgress;

    public void Initialize()
    {
        _sawmill = _logs.GetSawmill("screencheck");

        _net.RegisterNetMessage<MsgScreenCheckRequest>(OnRequestReceived);
        _net.RegisterNetMessage<MsgScreenCheckResponse>();
    }

    private void OnRequestReceived(MsgScreenCheckRequest message)
    {
        if (Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0)
        {
            _sawmill.Warning("Rejected screencheck request {0}: another capture is already in progress.", message.RequestId);
            SendFailure(message.RequestId, ScreenCheckCaptureFailureReason.Busy);
            return;
        }

        try
        {
            _clyde.Screenshot(ScreenshotType.Final, screenshot => _ = ProcessScreenshotAsync(message.RequestId, screenshot));
        }
        catch (Exception e)
        {
            Interlocked.Exchange(ref _captureInProgress, 0);
            _sawmill.Error("Failed to queue screencheck screenshot for request {0}: {1}", message.RequestId, e);
            SendFailure(message.RequestId, ScreenCheckCaptureFailureReason.ReadbackFailed);
        }
    }

    private async Task ProcessScreenshotAsync(uint requestId, Image<Rgb24> screenshot)
    {
        using (screenshot)
        {
            try
            {
                var result = await Task.Run(() => new EncodedScreenshot(
                    EncodeScreenshot(screenshot),
                    IsLikelyBlack(screenshot)));

                if (result.IsLikelyBlack)
                {
                    _sawmill.Warning("Screencheck request {0} produced an effectively black frame.", requestId);
                }

                _net.ClientSendMessage(new MsgScreenCheckResponse
                {
                    RequestId = requestId,
                    Success = true,
                    IsLikelyBlack = result.IsLikelyBlack,
                    ImageData = result.ImageData
                });
            }
            catch (Exception e)
            {
                _sawmill.Error("Failed to encode screencheck screenshot for request {0}: {1}", requestId, e);
                SendFailure(requestId, ScreenCheckCaptureFailureReason.EncodingFailed);
            }
            finally
            {
                Interlocked.Exchange(ref _captureInProgress, 0);
            }
        }
    }

    private static byte[] EncodeScreenshot(Image<Rgb24> screenshot)
    {
        if (!ScreenCheckImageValidator.IsAllowedDimensions(screenshot.Width, screenshot.Height))
            throw new InvalidOperationException($"Screencheck screenshot dimensions exceed limit: {screenshot.Width}x{screenshot.Height}.");

        using var stream = new MemoryStream();
        screenshot.SaveAsJpeg(stream);

        if (stream.Length <= 0 || stream.Length > ScreenCheckImageValidator.MaxImageBytes)
            throw new InvalidOperationException($"Screencheck screenshot size exceeds limit: {stream.Length} bytes.");

        return stream.ToArray();
    }

    private void SendFailure(uint requestId, ScreenCheckCaptureFailureReason reason)
    {
        _net.ClientSendMessage(new MsgScreenCheckResponse
        {
            RequestId = requestId,
            Success = false,
            FailureReason = reason
        });
    }

    private static bool IsLikelyBlack(Image<Rgb24> screenshot)
    {
        const int maxSamples = 4096;

        var pixels = screenshot.GetPixelSpan();
        if (pixels.IsEmpty)
            return false;

        var sampleStep = Math.Max(1, pixels.Length / maxSamples);
        var sampledRgb = new byte[((pixels.Length + sampleStep - 1) / sampleStep) * 3];
        var sampleOffset = 0;
        for (var pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex += sampleStep)
        {
            var pixel = pixels[pixelIndex];
            sampledRgb[sampleOffset++] = pixel.R;
            sampledRgb[sampleOffset++] = pixel.G;
            sampledRgb[sampleOffset++] = pixel.B;
        }

        return ScreenCheckImageValidator.IsLikelyBlackRgb(sampledRgb);
    }

    private readonly record struct EncodedScreenshot(byte[] ImageData, bool IsLikelyBlack);
}
