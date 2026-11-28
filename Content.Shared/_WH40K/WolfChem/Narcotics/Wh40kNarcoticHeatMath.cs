namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>How hot a narcotics seller is, which sets how much a tip about them gives away.</summary>
public enum Wh40kNarcoticHeatTier : byte
{
    Cold,
    Warm,
    Hot,
}

/// <summary>The sums behind narcotics heat: what a buyer pays a seller at a given heat, and how heat cools.</summary>
public static class Wh40kNarcoticHeatMath
{
    /// <summary>
    /// What a buyer pays for <paramref name="value"/> of narcotics from a seller at <paramref name="heat"/>.
    /// </summary>
    public static double Payout(double heat, double value, double free, double scale, double floor,
        double cap = double.PositiveInfinity, double glut = 0)
    {
        if (value <= 0)
            return 0;

        heat = Math.Max(0, heat);
        var saturated = Over(heat, value, cap) * Math.Clamp(glut, 0, 1);
        var end = Math.Min(heat + value, cap);
        return end > heat ? saturated + Curve(heat, end, free, scale, floor) : saturated;
    }

    private static double Curve(double heat, double end, double free, double scale, double floor)
    {
        free = Math.Max(0, free);
        floor = Math.Clamp(floor, 0, 1);

        var paid = Math.Max(0, Math.Min(end, free) - heat);
        if (end <= free)
            return paid;

        var start = Math.Max(heat, free);
        if (scale <= 0)
            return paid + (end - start) * floor;

        var bottom = floor > 0 ? free + scale * Math.Log(1 / floor) : double.PositiveInfinity;
        var falling = Math.Min(end, bottom);
        if (falling > start)
            paid += scale * (Math.Exp(-(start - free) / scale) - Math.Exp(-(falling - free) / scale));

        if (end > bottom)
            paid += (end - Math.Max(start, bottom)) * floor;

        return paid;
    }

    /// <summary>How much of a sale of <paramref name="value"/> from a seller at <paramref name="heat"/> lies past a line.</summary>
    public static double Over(double heat, double value, double line)
    {
        return Math.Max(0, heat + value - Math.Max(heat, line));
    }

    /// <summary>Heat after cooling for some seconds at <paramref name="rate"/> a second.</summary>
    public static double Cool(double heat, double seconds, double rate)
    {
        return Math.Max(0, heat - Math.Max(0, seconds) * Math.Max(0, rate));
    }

    /// <summary>The tier a heat falls in: cold while sales still pay in full, hot past <paramref name="hot"/>.</summary>
    public static Wh40kNarcoticHeatTier Tier(double heat, double free, double hot)
    {
        if (heat > Math.Max(free, hot))
            return Wh40kNarcoticHeatTier.Hot;

        return heat > free ? Wh40kNarcoticHeatTier.Warm : Wh40kNarcoticHeatTier.Cold;
    }
}
