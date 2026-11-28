using System.Linq;
using Content.Shared._WH40K.Augments;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Augments;

internal static class Wh40kAugmentInfo
{
    public static IEnumerable<(EntityPrototype Prototype, Wh40kAugmentBedZone Zone)> Catalogue(IPrototypeManager prototypes, IComponentFactory factory)
    {
        return prototypes.EnumeratePrototypes<EntityPrototype>()
            .Where(prototype => !prototype.Abstract)
            .SelectMany(prototype => Wh40kAugmentCatalog.Zones(prototype, factory).Select(zone => (prototype, zone)));
    }

    public static string Describe(EntityPrototype prototype, IComponentFactory factory)
    {
        if (prototype.TryComp<Wh40kProstheticComponent>(out var prosthetic, factory)
            && prototype.TryComp<BodyPartComponent>(out var part, factory))
        {
            var walking = part.PartType is BodyPartType.Leg or BodyPartType.Foot;
            var result = Loc.GetString(walking ? "wh40k-augment-info-leg" : "wh40k-augment-info-arm", ("percent", prosthetic.BonusPercent));
            if (part.PartType == BodyPartType.Leg && prosthetic.BonusPercent == 10)
                result += "\n" + Loc.GetString("wh40k-augments-servo-step-effect");
            return result;
        }

        if (prototype.TryComp<OrganComponent>(out var organ, factory)
            && organ.SlotId is Wh40kAugmentCatalog.LeftEye or Wh40kAugmentCatalog.RightEye)
        {
            return Loc.GetString(organ.OnAdd?.ContainsKey("ShowHealthBars") == true
                ? "wh40k-augment-info-eye-archeotech"
                : "wh40k-augment-info-eye-red");
        }

        return string.IsNullOrWhiteSpace(prototype.Description)
            ? Loc.GetString("wh40k-augment-info-no-description")
            : prototype.Description;
    }
}
