using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client.Graphics;

/// <summary>
/// Splits independent primitive lists into renderer-safe draw calls.
/// </summary>
internal static class PrimitiveDrawingExtensions
{
    // Clyde batches a finite amount of vertex data. Keep content-side draws well below that
    // capacity instead of allowing a large map, network, or graph to overflow one draw call.
    private const int MaximumVerticesPerDraw = 3 * 4096;

    /// <summary>
    /// Draws an arbitrary number of independent lines or triangles without splitting a primitive.
    /// </summary>
    public static void DrawPrimitivesBatched(
        this DrawingHandleBase handle,
        DrawPrimitiveTopology primitiveTopology,
        ReadOnlySpan<Vector2> vertices,
        Color color)
    {
        if (vertices.IsEmpty)
            return;

        var verticesPerPrimitive = primitiveTopology switch
        {
            DrawPrimitiveTopology.LineList => 2,
            DrawPrimitiveTopology.TriangleList => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(primitiveTopology), primitiveTopology, null),
        };

        var batchSize = MaximumVerticesPerDraw / verticesPerPrimitive * verticesPerPrimitive;
        for (var offset = 0; offset < vertices.Length; offset += batchSize)
        {
            var count = Math.Min(batchSize, vertices.Length - offset);
            handle.DrawPrimitives(primitiveTopology, vertices.Slice(offset, count), color);
        }
    }
}
