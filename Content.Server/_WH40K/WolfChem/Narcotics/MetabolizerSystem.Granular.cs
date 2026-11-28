using Content.Server._WH40K.WolfChem.Narcotics;
using Content.Shared.FixedPoint;

namespace Content.Server.Body.Systems;

public sealed partial class MetabolizerSystem
{
    private bool _wh40kCarried;

    /// <summary>
    /// The amount of a reagent to remove this tick at a rate times an organ's modifier, worked out in
    /// ten-thousandths of a unit. What doesn't make a whole hundredth is carried on the organ to the next tick.
    /// <paramref name="scale"/> is the share of a full tick the reagent left can pay for.
    /// </summary>
    private FixedPoint2 Wh40kGranularRate(EntityUid organ, string reagent, string group, FixedPoint2 rate,
        FixedPoint2 modifier, FixedPoint2 quantity, out float scale)
    {
        _wh40kCarried = false;
        var product = (long) rate.Value * modifier.Value;
        if (product <= 0 || quantity <= FixedPoint2.Zero)
        {
            scale = 0f;
            return FixedPoint2.Zero;
        }

        scale = Math.Min(quantity.Value * 100L, product) / (float) product;
        if (product % 100 == 0)
            return FixedPoint2.Min(FixedPoint2.FromCents((int) Math.Min(product / 100, int.MaxValue)), quantity);

        var carry = EnsureComp<Wh40kMetabolismCarryComponent>(organ);
        var key = (reagent, group);
        var owed = carry.Owed.GetValueOrDefault(key) + product;
        if (owed / 100 >= quantity.Value)
        {
            carry.Owed.Remove(key);
            return quantity;
        }

        carry.Owed[key] = (int) (owed % 100);
        _wh40kCarried = owed < 100;
        return FixedPoint2.FromCents((int) (owed / 100));
    }

    /// <summary>
    /// True once after a <see cref="Wh40kGranularRate"/> call that removed nothing and only carried its rate over, so the
    /// reagent still counts towards the organ's limit on that tick.
    /// </summary>
    private bool Wh40kTakeCarried()
    {
        var carried = _wh40kCarried;
        _wh40kCarried = false;
        return carried;
    }
}
