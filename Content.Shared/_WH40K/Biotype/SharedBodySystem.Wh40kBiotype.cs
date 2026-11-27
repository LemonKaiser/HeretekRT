using System.Linq;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Body.Part;
using Content.Shared.Body.Components;
using Content.Shared.Body.Prototypes;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Shared.Body.Systems;

public partial class SharedBodySystem
{
    /// <summary>
    /// Replaces a body's parts and organs with a fresh build of another body prototype without
    /// triggering injury/amputation/brain-removal side effects. Held hand items remain intact where
    /// the new body provides the same hands.
    /// </summary>
    public void Wh40kRebuildBody(EntityUid body, ProtoId<BodyPrototype> prototype, BodyComponent? comp = null)
    {
        if (!Resolve(body, ref comp, false) || TerminatingOrDeleted(body) || !Prototypes.HasIndex(prototype))
            return;

        if (LifeStage(body) < EntityLifeStage.MapInitialized)
        {
            comp.Prototype = prototype;
            Dirty(body, comp);
            return;
        }

        var parts = GetBodyChildren(body, comp).ToList();
        var organs = GetBodyOrgans(body, comp).ToList();

        foreach (var (part, partComp) in parts)
        {
            if (partComp.Enabled && (partComp.OnAdd != null || partComp.OnRemove != null))
                RaiseLocalEvent(part, new BodyPartComponentsModifyEvent(body, false));
        }

        foreach (var (organ, organComp) in organs)
        {
            if (organComp.Enabled && (organComp.OnAdd != null || organComp.OnRemove != null))
                RaiseLocalEvent(organ, new OrganComponentsModifyEvent(body, false));
        }

        foreach (var (organ, organComp) in organs)
        {
            organComp.Body = null;
            Dirty(organ, organComp);
        }

        foreach (var (part, partComp) in parts)
        {
            partComp.Body = null;
            Dirty(part, partComp);
        }

        var root = comp.RootContainer.ContainedEntity;
        var fresh = new BodyComponent
        {
            Prototype = prototype,
            GibSound = comp.GibSound,
            RequiredLegs = comp.RequiredLegs,
            ThermalVisibility = comp.ThermalVisibility,
        };

        RemComp(body, comp);
        if (root != null)
            Del(root.Value);

        AddComp(body, fresh);
        RefreshBaseLayersFromParts(body, fresh);
    }

    /// <summary>
    /// Ensures every attached part with a custom <see cref="BodyPartComponent.BaseLayerId"/>
    /// (such as cybernetic prosthetics) is reflected in <see cref="HumanoidAppearanceComponent.CustomBaseLayers"/>.
    /// </summary>
    public void RefreshBaseLayersFromParts(EntityUid body, BodyComponent? comp = null)
    {
        if (!Resolve(body, ref comp, false) ||
            TerminatingOrDeleted(body) ||
            !HasComp<HumanoidAppearanceComponent>(body))
        {
            return;
        }

        foreach (var (partId, partComp) in GetBodyChildren(body, comp))
        {
            var partAppearance = EnsureComp<BodyPartAppearanceComponent>(partId);
            if (partComp.BaseLayerId == null || partAppearance.ID == null)
                continue;

            _humanoid.SetBaseLayerId(body, partAppearance.Type, partAppearance.ID, sync: true);
            UpdateAppearance(body, partAppearance);
        }
    }
}
