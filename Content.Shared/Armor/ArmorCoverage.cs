using Content.Shared.Inventory;
using Content.Shared._Shitmed.Targeting;

namespace Content.Shared.Armor;

/// <summary>
/// Shared helpers for resolving which body parts an armor item protects.
/// </summary>
public static class ArmorCoverage
{
    public static bool Covers(TargetBodyPart coverage, TargetBodyPart? targetPart)
    {
        if (coverage == 0)
            return false;

        if (!targetPart.HasValue || targetPart.Value == TargetBodyPart.All)
            return true;

        return (coverage & targetPart.Value) != 0;
    }

    /// <summary>
    /// Infers a conservative body-part mask from an item's clothing slot.
    /// </summary>
    public static TargetBodyPart FromSlots(SlotFlags slots)
    {
        if (slots == SlotFlags.All)
            return TargetBodyPart.All;

        var coverage = (TargetBodyPart) 0;

        if ((slots & (SlotFlags.HEAD |
                      SlotFlags.EYES |
                      SlotFlags.EARS |
                      SlotFlags.MASK |
                      SlotFlags.BALACLAVA |
                      SlotFlags.HELMETCOVER |
                      SlotFlags.HELMETATTACHMENT)) != 0)
        {
            coverage |= TargetBodyPart.Head;
        }

        if ((slots & SlotFlags.OUTERCLOTHING) != 0)
        {
            coverage |= TargetBodyPart.Torso | TargetBodyPart.Arms | TargetBodyPart.Legs;
        }

        if ((slots & (SlotFlags.INNERCLOTHING |
                      SlotFlags.NECK |
                      SlotFlags.BACK |
                      SlotFlags.SUITSTORAGE)) != 0)
        {
            coverage |= TargetBodyPart.Torso;
        }

        if ((slots & SlotFlags.GLOVES) != 0)
            coverage |= TargetBodyPart.Hands;

        if ((slots & (SlotFlags.ARMBANDLEFT | SlotFlags.ARMBANDRIGHT)) != 0)
            coverage |= TargetBodyPart.Arms;

        if ((slots & SlotFlags.LEGS) != 0)
            coverage |= TargetBodyPart.Legs;

        if ((slots & SlotFlags.FEET) != 0)
            coverage |= TargetBodyPart.Feet;

        return coverage;
    }
}
