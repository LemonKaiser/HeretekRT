using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared._WH40K.Augments;
using Content.Shared.Body.Part;
using Content.Shared.Damage;

namespace Content.Shared._WH40K.Biotype;

public abstract class SharedWh40kBiotypeSystem : EntitySystem
{
    public Wh40kBiotype GetBiotype(EntityUid uid)
    {
        if (TryComp<Wh40kBiotypeComponent>(uid, out var comp))
            return comp.Biotype;

        if (HasComp<SiliconComponent>(uid))
            return Wh40kBiotype.Cybernetic;

        return Wh40kBiotype.Organic;
    }

    public bool IsCybernetic(EntityUid uid)
    {
        return GetBiotype(uid) == Wh40kBiotype.Cybernetic;
    }

    /// <summary>
    /// Unified check for whether a body part is synthetic/prosthetic (cheap/adamantium/reinforced WH40K prosthetics,
    /// generic Shitmed cybernetics, or inorganic/silicon chassis parts).
    /// </summary>
    public bool IsSyntheticPart(EntityUid partUid)
    {
        if (!HasComp<BodyPartComponent>(partUid))
            return false;

        if (HasComp<Wh40kProstheticComponent>(partUid) || HasComp<CyberneticsComponent>(partUid))
            return true;

        return TryComp<DamageableComponent>(partUid, out var damageable) &&
               damageable.DamageContainerID?.Id is "Inorganic" or "Silicon";
    }
}
