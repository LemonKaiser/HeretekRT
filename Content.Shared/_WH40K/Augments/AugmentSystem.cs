using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Interaction;

namespace Content.Shared._WH40K.Augments;

/// <summary>
/// Maintains the body-to-augment relationship and permits an owner to access their own implants.
/// </summary>
public sealed class AugmentSystem : EntitySystem
{
    private EntityQuery<InstalledAugmentsComponent> _installedQuery;
    private EntityQuery<OrganComponent> _organQuery;

    public override void Initialize()
    {
        _installedQuery = GetEntityQuery<InstalledAugmentsComponent>();
        _organQuery = GetEntityQuery<OrganComponent>();

        SubscribeLocalEvent<AugmentComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<InstalledAugmentsComponent, AccessibleOverrideEvent>(OnAccessibleOverride);
    }

    private void OnOrganAddedToBody(Entity<AugmentComponent> augment, ref OrganAddedToBodyEvent args)
    {
        var installed = EnsureComp<InstalledAugmentsComponent>(args.Body);
        if (installed.InstalledAugments.Add(GetNetEntity(augment)))
            Dirty(args.Body, installed);
    }

    private void OnOrganRemovedFromBody(Entity<AugmentComponent> augment, ref OrganRemovedFromBodyEvent args)
    {
        if (!TryComp<InstalledAugmentsComponent>(args.OldBody, out var installed) ||
            !installed.InstalledAugments.Remove(GetNetEntity(augment)))
        {
            return;
        }

        if (installed.InstalledAugments.Count == 0)
            RemComp<InstalledAugmentsComponent>(args.OldBody);
        else
            Dirty(args.OldBody, installed);
    }

    private void OnAccessibleOverride(Entity<InstalledAugmentsComponent> ent, ref AccessibleOverrideEvent args)
    {
        if (GetBody(args.Target) != args.User)
            return;

        args.Handled = true;
        args.Accessible = true;
    }

    /// <summary>
    /// Returns the body containing this augment, or null while it is not installed.
    /// </summary>
    public EntityUid? GetBody(EntityUid augment) => _organQuery.CompOrNull(augment)?.Body;

    /// <summary>
    /// Relays an event to every installed augment of a body.
    /// </summary>
    public void RelayEvent<T>(EntityUid body, ref T ev) where T : notnull
    {
        if (_installedQuery.TryComp(body, out var installed))
            RelayEvent((body, installed), ref ev);
    }

    public void RelayEvent<T>(Entity<InstalledAugmentsComponent> body, ref T ev) where T : notnull
    {
        foreach (var netEntity in body.Comp.InstalledAugments)
        {
            var augment = GetEntity(netEntity);
            if (!Exists(augment))
                continue;

            RaiseLocalEvent(augment, ref ev);
        }
    }
}
