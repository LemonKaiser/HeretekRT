namespace Content.Shared._WH40K.Economy;

public static class MaterialMarketMath
{
    public static double Recover(double load, double seconds, double rate)
    {
        if (!double.IsFinite(load) || load < 0 || !double.IsFinite(seconds) || !double.IsFinite(rate) || rate < 0)
            return 0;
        return Math.Max(0, load - Math.Max(0, seconds) * rate);
    }

    public static bool TryQuote(double load, double quantity, double price, double coefficient,
        double grace, double scale, double minimum, out double amount, double demand = 1)
    {
        amount = 0;
        if (!Valid(load) || !Valid(quantity) || !Valid(price) || !Valid(coefficient) || !Valid(grace) ||
            !double.IsFinite(scale) || scale <= 0 || !double.IsFinite(minimum) || minimum <= 0 || minimum > 1 ||
            !double.IsFinite(demand) || demand < minimum)
            return false;

        var free = Math.Min(quantity, Math.Max(0, grace - load));
        var start = Math.Max(0, load - grace);
        var pressured = quantity - free;
        var ratio = pressured / (scale + start);
        var logarithm = ratio < 1e-8 ? ratio * (1 - ratio / 2 + ratio * ratio / 3) : Math.Log(1 + ratio);
        var integral = demand * free + minimum * pressured + (demand - minimum) * scale * logarithm;
        amount = Math.Min(quantity * demand, Math.Max(0, integral)) * price * coefficient;
        if (double.IsFinite(amount))
            return true;
        amount = 0;
        return false;
    }

    public static bool Valid(double value) => double.IsFinite(value) && value >= 0;

    public static int ToCash(double amount) => !Valid(amount) ? 0 :
        (int) Math.Min(int.MaxValue, Math.Floor(amount + 1e-7));

    public static double DemandFactor(double load, double grace, double scale, double minimum, double demand) =>
        minimum + (demand - minimum) / (1 + Math.Max(0, load - grace) / scale);
}
