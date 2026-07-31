using Content.Shared.Item.ItemToggle.Components;

namespace Content.Shared.Item.ItemToggle;

/// <summary>
/// Handles <see cref="ComponentTogglerComponent"/> component manipulation.
/// </summary>
public sealed class ComponentTogglerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ComponentTogglerComponent, ItemToggledEvent>(OnToggled);
    }

    private void OnToggled(Entity<ComponentTogglerComponent> ent, ref ItemToggledEvent args)
    {
        ToggleComponent(ent, args.Activated);
    }

    // Goobstation - Make this system more flexible
    public void ToggleComponent(EntityUid uid, bool activate)
    {
        if (!TryComp<ComponentTogglerComponent>(uid, out var component))
            return;

        var target = component.Parent ? Transform(uid).ParentUid : uid;

        if (activate)
            EntityManager.AddComponents(target, component.Components);
        else
            EntityManager.RemoveComponents(target, component.RemoveComponents ?? component.Components);
    }

    /// <summary>
    /// Returns whether this entity's toggle configuration owns a runtime component.
    /// Parent-targeted configurations are excluded because they do not modify the item itself.
    /// </summary>
    public bool ManagesRuntimeComponent(EntityUid uid, string componentId)
    {
        if (!TryComp<ComponentTogglerComponent>(uid, out var component) || component.Parent)
            return false;

        return component.Components.ContainsKey(componentId) ||
               component.RemoveComponents?.ContainsKey(componentId) == true;
    }

    /// <summary>
    /// Returns whether toggling this entity would mutate its parent instead of the item itself.
    /// </summary>
    public bool TargetsParent(EntityUid uid)
    {
        return TryComp<ComponentTogglerComponent>(uid, out var component) && component.Parent;
    }
}
