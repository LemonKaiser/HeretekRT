using System.Linq;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Implants.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Augments;

public static class Wh40kAugmentCatalog
{
    public const string Eyes = "eyes";
    public const string LeftEye = "leftEye";
    public const string RightEye = "rightEye";

    public static bool IsOpticalSlot(string slot) => slot is Eyes or LeftEye or RightEye;

    public static bool IsNativeMechanicalOrgan(EntityPrototype prototype)
        => prototype.Parents?.Contains("BaseIPCOrgan") == true;

    public static IEnumerable<Wh40kAugmentBedZone> Zones(EntityPrototype prototype, IComponentFactory factory)
    {
        var cybernetic = prototype.TryGetComponent<CyberneticsComponent>(out _, factory);
        if ((cybernetic || prototype.TryGetComponent<Wh40kProstheticComponent>(out _, factory))
            && prototype.TryGetComponent<BodyPartComponent>(out var part, factory))
        {
            if (LimbZone(part) is { } zone)
                yield return zone;
        }
        else if ((cybernetic || IsNativeMechanicalOrgan(prototype))
                 && prototype.TryGetComponent<OrganComponent>(out var organ, factory))
        {
            switch (organ.SlotId)
            {
                case LeftEye: yield return Wh40kAugmentBedZone.LeftEye; break;
                case RightEye: yield return Wh40kAugmentBedZone.RightEye; break;
                case Eyes: yield return Wh40kAugmentBedZone.Eyes; break;
                case "heart" or "lungs": yield return Wh40kAugmentBedZone.Core; break;
            }
        }
        else if (prototype.TryGetComponent<AugmentArmComponent>(out _, factory))
        {
            yield return Wh40kAugmentBedZone.LeftArm;
            yield return Wh40kAugmentBedZone.RightArm;
        }
        else if (prototype.TryGetComponent<AugmentPowerCellSlotComponent>(out _, factory)
                 || prototype.TryGetComponent<SubdermalImplantComponent>(out _, factory))
        {
            yield return Wh40kAugmentBedZone.Core;
        }
    }

    public static bool IsInZone(Wh40kAugmentBedZone zone, string mapZone) => zone switch
    {
        Wh40kAugmentBedZone.Eyes => mapZone is "left-eye" or "right-eye",
        _ => MapZone(zone) == mapZone,
    };

    public static string MapZone(Wh40kAugmentBedZone zone) => zone switch
    {
        Wh40kAugmentBedZone.LeftEye => "left-eye",
        Wh40kAugmentBedZone.Eyes or Wh40kAugmentBedZone.RightEye => "right-eye",
        Wh40kAugmentBedZone.LeftArm or Wh40kAugmentBedZone.LeftHand => "left-arm",
        Wh40kAugmentBedZone.RightArm or Wh40kAugmentBedZone.RightHand => "right-arm",
        Wh40kAugmentBedZone.LeftLeg or Wh40kAugmentBedZone.LeftFoot => "left-leg",
        Wh40kAugmentBedZone.RightLeg or Wh40kAugmentBedZone.RightFoot => "right-leg",
        _ => "core",
    };

    private static Wh40kAugmentBedZone? LimbZone(BodyPartComponent part) => (part.PartType, part.Symmetry) switch
    {
        (BodyPartType.Arm, BodyPartSymmetry.Left) => Wh40kAugmentBedZone.LeftArm,
        (BodyPartType.Arm, BodyPartSymmetry.Right) => Wh40kAugmentBedZone.RightArm,
        (BodyPartType.Leg, BodyPartSymmetry.Left) => Wh40kAugmentBedZone.LeftLeg,
        (BodyPartType.Leg, BodyPartSymmetry.Right) => Wh40kAugmentBedZone.RightLeg,
        (BodyPartType.Hand, BodyPartSymmetry.Left) => Wh40kAugmentBedZone.LeftHand,
        (BodyPartType.Hand, BodyPartSymmetry.Right) => Wh40kAugmentBedZone.RightHand,
        (BodyPartType.Foot, BodyPartSymmetry.Left) => Wh40kAugmentBedZone.LeftFoot,
        (BodyPartType.Foot, BodyPartSymmetry.Right) => Wh40kAugmentBedZone.RightFoot,
        _ => null,
    };
}
