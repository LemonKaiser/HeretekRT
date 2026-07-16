namespace Content.Server._Mono.LongRun;

/// <summary>
/// Keeps a fixed-size, allocation-free history for detecting slow accumulation over long rounds.
/// Linear regression makes the result less sensitive to GC and player-count noise than comparing
/// only the oldest and newest samples.
/// </summary>
internal sealed class LongRunTrendTracker
{
    // 97 samples at a 15 minute interval cover a full 24 hour window including both endpoints.
    private const int Capacity = 97;

    private readonly TrendSample[] _samples = new TrendSample[Capacity];
    private int _nextIndex;
    private int _count;

    public void Clear()
    {
        _nextIndex = 0;
        _count = 0;
    }

    public void Add(
        TimeSpan observedAt,
        long managedHeapBytes,
        long workingSetBytes,
        int entityCount,
        int biomeLoadedChunks,
        int worldgenChunks)
    {
        _samples[_nextIndex] = new TrendSample(
            observedAt,
            managedHeapBytes,
            workingSetBytes,
            entityCount,
            biomeLoadedChunks,
            worldgenChunks);
        _nextIndex = (_nextIndex + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    public LongRunTrendSnapshot GetTrend()
    {
        if (_count < 2)
            return LongRunTrendSnapshot.Empty;

        var oldest = GetSample(0);
        var newest = GetSample(_count - 1);
        var windowHours = (newest.ObservedAt - oldest.ObservedAt).TotalHours;
        if (windowHours <= 0d)
            return LongRunTrendSnapshot.Empty;

        return new LongRunTrendSnapshot(
            _count,
            windowHours,
            CalculateSlope(TrendMetric.ManagedHeap),
            CalculateSlope(TrendMetric.WorkingSet),
            CalculateSlope(TrendMetric.Entities),
            CalculateSlope(TrendMetric.BiomeLoadedChunks),
            CalculateSlope(TrendMetric.WorldgenChunks));
    }

    private double CalculateSlope(TrendMetric metric)
    {
        var baseline = GetSample(0).ObservedAt;
        double sumX = 0d;
        double sumY = 0d;
        double sumXy = 0d;
        double sumXSquared = 0d;
        var validSamples = 0;

        for (var i = 0; i < _count; i++)
        {
            var sample = GetSample(i);
            var x = (sample.ObservedAt - baseline).TotalHours;
            var y = GetValue(sample, metric);
            if (!double.IsFinite(y))
                continue;

            sumX += x;
            sumY += y;
            sumXy += x * y;
            sumXSquared += x * x;
            validSamples++;
        }

        if (validSamples < 2)
            return 0d;

        var denominator = validSamples * sumXSquared - sumX * sumX;
        if (Math.Abs(denominator) < double.Epsilon)
            return 0d;

        return (validSamples * sumXy - sumX * sumY) / denominator;
    }

    private TrendSample GetSample(int chronologicalIndex)
    {
        var oldestIndex = (_nextIndex - _count + Capacity) % Capacity;
        return _samples[(oldestIndex + chronologicalIndex) % Capacity];
    }

    private static double GetValue(TrendSample sample, TrendMetric metric)
    {
        return metric switch
        {
            TrendMetric.ManagedHeap => sample.ManagedHeapBytes,
            TrendMetric.WorkingSet => sample.WorkingSetBytes > 0 ? sample.WorkingSetBytes : double.NaN,
            TrendMetric.Entities => sample.EntityCount,
            TrendMetric.BiomeLoadedChunks => sample.BiomeLoadedChunks,
            TrendMetric.WorldgenChunks => sample.WorldgenChunks,
            _ => 0d,
        };
    }

    private readonly record struct TrendSample(
        TimeSpan ObservedAt,
        long ManagedHeapBytes,
        long WorkingSetBytes,
        int EntityCount,
        int BiomeLoadedChunks,
        int WorldgenChunks);

    private enum TrendMetric : byte
    {
        ManagedHeap,
        WorkingSet,
        Entities,
        BiomeLoadedChunks,
        WorldgenChunks,
    }
}

internal readonly record struct LongRunTrendSnapshot(
    int SampleCount,
    double WindowHours,
    double ManagedHeapBytesPerHour,
    double WorkingSetBytesPerHour,
    double EntitiesPerHour,
    double BiomeLoadedChunksPerHour,
    double WorldgenChunksPerHour)
{
    public static LongRunTrendSnapshot Empty => default;
}
