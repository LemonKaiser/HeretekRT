using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;

namespace Content.Server._WH40K.Augments;

public sealed partial class Wh40kCyberneticEyeSystem : EntitySystem
{
    [Dependency] private SharedHumanoidAppearanceSystem _appearance = default!;
    [Dependency] private SharedBodySystem _body = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<Wh40kCyberneticEyeComponent, OrganAddedToBodyEvent>(OnAdded);
        SubscribeLocalEvent<Wh40kCyberneticEyeComponent, OrganRemovedFromBodyEvent>(OnRemoved);
    }

    private void OnAdded(Entity<Wh40kCyberneticEyeComponent> ent, ref OrganAddedToBodyEvent args)
    {
        if (TryComp(args.Body, out HumanoidAppearanceComponent? appearance))
            _appearance.AddMarking(args.Body, ent.Comp.Marking.Id, Color.White, forced: true, humanoid: appearance);
    }

    private void OnRemoved(Entity<Wh40kCyberneticEyeComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (TryComp(args.OldBody, out HumanoidAppearanceComponent? appearance)
            && appearance.MarkingSet.Remove(MarkingCategories.Overlay, ent.Comp.Marking.Id))
        {
            Dirty(args.OldBody, appearance);
        }

        foreach (var remaining in _body.GetBodyOrganEntityComps<Wh40kCyberneticEyeComponent>(args.OldBody))
        {
            if (remaining.Owner == ent.Owner || !remaining.Comp2.Enabled)
                continue;

            var refresh = new OrganComponentsModifyEvent(args.OldBody, true);
            RaiseLocalEvent(remaining.Owner, refresh);
        }
    }
}
