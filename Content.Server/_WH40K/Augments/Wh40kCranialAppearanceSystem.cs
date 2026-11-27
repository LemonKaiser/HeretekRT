using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Augments;

/// <summary>Applies the appearance of an installed cranial augment to its host.</summary>
[RegisterComponent]
public sealed partial class Wh40kCranialAppearanceComponent : Component
{
    [DataField]
    public ProtoId<MarkingPrototype>? Marking;

    [DataField]
    public ProtoId<HumanoidSpeciesSpriteLayer>? HeadSprite;

    [DataField]
    public ProtoId<HumanoidSpeciesSpriteLayer>? PreviousHeadSprite;
}

public sealed partial class Wh40kCranialAppearanceSystem : EntitySystem
{
    [Dependency] private SharedHumanoidAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kCranialAppearanceComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kCranialAppearanceComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<Wh40kCranialAppearanceComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp(ent, out HumanoidAppearanceComponent? humanoid))
            return;

        if (ent.Comp.Marking is { } marking)
        {
            humanoid.MarkingSet.Remove(MarkingCategories.Overlay, marking.Id);
            _appearance.AddMarking(ent, marking.Id, Color.White, forced: true, humanoid: humanoid);
        }

        var current = humanoid.CustomBaseLayers.GetValueOrDefault(HumanoidVisualLayers.Head).Id;
        if (ent.Comp.HeadSprite is { } sprite && current != sprite)
        {
            ent.Comp.PreviousHeadSprite = current;
            _appearance.SetBaseLayerId(ent, HumanoidVisualLayers.Head, sprite.Id, humanoid: humanoid);
        }
    }

    private void OnShutdown(Entity<Wh40kCranialAppearanceComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent) || !TryComp(ent, out HumanoidAppearanceComponent? humanoid))
            return;

        if (ent.Comp.Marking is { } marking && humanoid.MarkingSet.Remove(MarkingCategories.Overlay, marking.Id))
            Dirty(ent, humanoid);

        if (ent.Comp.HeadSprite is { } sprite && humanoid.CustomBaseLayers.GetValueOrDefault(HumanoidVisualLayers.Head).Id == sprite)
            _appearance.SetBaseLayerId(ent, HumanoidVisualLayers.Head, ent.Comp.PreviousHeadSprite?.Id, humanoid: humanoid);
    }
}
