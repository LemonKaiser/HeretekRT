using Content.Client.Clickable;
using Content.Client._WH40K.Visuals.WorldEffects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._WH40K.Administration;

/// <summary>Resolves eye islands on demand from the click masks already retained by the client.</summary>
internal sealed class UndeadEyeFrameCache(IClickMapManager pixels, RsiVisualGeometryCache geometry)
{
    private readonly Dictionary<(RSI.State, RsiDirection, int), EyeFrameGeometry> _frames = new();

    public EyeFrameGeometry Get(RSI.State state, RsiDirection direction, int frame, OpaqueGeometry head)
    {
        var opaque = geometry.GetOpaqueGeometry(state, direction, frame);
        if (!UndeadEyeGeometry.IsEyeMask(opaque, head))
            return default;

        var key = (state, direction, frame);
        if (_frames.TryGetValue(key, out var cached))
            return cached;

        var result = UndeadEyeGeometry.AnalyzeFrame(state.RSI.Size.X, state.RSI.Size.Y, opaque,
            (x, y) => pixels.IsOccluding(state.RSI, state.StateId, direction, frame, new Vector2i(x, y), true));
        _frames.Add(key, result);
        return result;
    }
}
