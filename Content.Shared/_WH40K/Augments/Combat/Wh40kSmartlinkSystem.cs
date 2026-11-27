using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Emp;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Compensates recoil and boosts projectile speed for guns held in a smartlinked hand,
/// and scrambles the link during EMP interference.
/// </summary>
public sealed class Wh40kSmartlinkSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kSmartlinkComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kSmartlinkComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kSmartlinkComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<Wh40kSmartlinkComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);
        SubscribeLocalEvent<Wh40kSmartlinkComponent, BodyPartComponentsModifyEvent>(OnPartModify);
        SubscribeLocalEvent<Wh40kSmartlinkComponent, EmpPulseEvent>(OnEmpPulse);
        SubscribeLocalEvent<Wh40kSmartlinkUserComponent, GunRefreshModifiersEvent>(OnRefresh);
    }

    public bool IsScrambled(Wh40kSmartlinkComponent link)
        => link.ScrambledUntil > _timing.CurTime;

    private void OnStartup(Entity<Wh40kSmartlinkComponent> ent, ref ComponentStartup args)
    {
        if (ResolveOwner(ent, out var body, out var handPart))
        {
            Mark(body, true);
            RefreshHeld(handPart);
        }
    }

    private void OnShutdown(Entity<Wh40kSmartlinkComponent> ent, ref ComponentShutdown args)
    {
        if (ResolveOwner(ent, out var body, out var handPart))
        {
            Mark(body, false, excluding: ent.Owner);
            RefreshHeld(handPart);
        }
    }

    private void OnOrganAdded(Entity<Wh40kSmartlinkComponent> ent, ref OrganAddedToBodyEvent args)
    {
        Mark(args.Body, true);
        RefreshHeld(args.Part);
    }

    private void OnOrganRemoved(Entity<Wh40kSmartlinkComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        Mark(args.OldBody, false, excluding: ent.Owner);
        RefreshHeld(args.OldPart);
    }

    private void OnPartModify(Entity<Wh40kSmartlinkComponent> ent, ref BodyPartComponentsModifyEvent args)
    {
        Mark(args.Body, args.Add, excluding: args.Add ? null : ent.Owner);
        RefreshHeld(ent.Owner);
    }

    private void OnEmpPulse(Entity<Wh40kSmartlinkComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        var until = _timing.CurTime + args.Duration;
        if (until > ent.Comp.ScrambledUntil)
        {
            ent.Comp.ScrambledUntil = until;
            Dirty(ent);
            if (ResolveOwner(ent, out _, out var handPart))
                RefreshHeld(handPart);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<Wh40kSmartlinkComponent>();
        while (query.MoveNext(out var uid, out var link))
        {
            if (link.ScrambledUntil == TimeSpan.Zero || now < link.ScrambledUntil)
                continue;

            link.ScrambledUntil = TimeSpan.Zero;
            Dirty(uid, link);
            if (ResolveOwner(uid, out _, out var handPart))
                RefreshHeld(handPart);
        }
    }

    private void OnRefresh(Entity<Wh40kSmartlinkUserComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (LinkFor(ent, args.Gun) is not { } link)
            return;

        var scrambled = IsScrambled(link);
        args.AngleIncrease *= scrambled ? link.ScrambledIncreaseMultiplier : link.IncreaseMultiplier;
        args.AngleDecay *= scrambled ? link.ScrambledDecayMultiplier : link.DecayMultiplier;
        args.CameraRecoilScalar *= scrambled ? link.ScrambledCameraRecoilMultiplier : link.CameraRecoilMultiplier;
        if (!scrambled)
            args.ProjectileSpeed *= link.ProjectileSpeedMultiplier;
    }

    /// <summary>
    /// Returns the smartlink component governing the hand holding <paramref name="gun"/>, if any.
    /// </summary>
    public Wh40kSmartlinkComponent? LinkFor(EntityUid body, EntityUid gun)
    {
        if (!HasComp<Wh40kSmartlinkUserComponent>(body) || !_hands.IsHolding(body, gun, out var hand))
            return null;

        return LinkInHand(body, hand.Name);
    }

    private Wh40kSmartlinkComponent? LinkInHand(EntityUid body, string handName)
    {
        foreach (var (partId, part) in _body.GetBodyChildrenOfType(body, BodyPartType.Hand))
        {
            if (!part.Enabled || HandName(part) != handName)
                continue;

            if (TryComp<Wh40kSmartlinkComponent>(partId, out var direct))
                return direct;

            foreach (var (organId, _) in _body.GetPartOrgans(partId, part))
            {
                if (TryComp<Wh40kSmartlinkComponent>(organId, out var organLink))
                    return organLink;
            }
        }

        return null;
    }

    private void Mark(EntityUid body, bool linked, EntityUid? excluding = null)
    {
        if (_net.IsClient || TerminatingOrDeleted(body))
            return;

        if (linked)
        {
            EnsureComp<Wh40kSmartlinkUserComponent>(body);
            return;
        }

        foreach (var (partId, part) in _body.GetBodyChildrenOfType(body, BodyPartType.Hand))
        {
            if (partId != excluding && HasComp<Wh40kSmartlinkComponent>(partId))
                return;

            foreach (var (organId, _) in _body.GetPartOrgans(partId, part))
            {
                if (organId != excluding && HasComp<Wh40kSmartlinkComponent>(organId))
                    return;
            }
        }

        RemComp<Wh40kSmartlinkUserComponent>(body);
    }

    private bool ResolveOwner(EntityUid uid, out EntityUid body, out EntityUid handPart)
    {
        body = default;
        handPart = default;

        if (TryComp<OrganComponent>(uid, out var organ) && organ.Body is { } organBody)
        {
            body = organBody;
            handPart = Transform(uid).ParentUid;
            return Exists(handPart);
        }

        if (TryComp<BodyPartComponent>(uid, out var part) && part.Body is { } partBody)
        {
            body = partBody;
            handPart = uid;
            return true;
        }

        return false;
    }

    private void RefreshHeld(EntityUid handPart)
    {
        if (!TryComp<BodyPartComponent>(handPart, out var part) ||
            part.Body is not { } body ||
            HandName(part) is not { } name ||
            !_hands.TryGetHand(body, name, out var held) ||
            !TryComp<GunComponent>(held.HeldEntity, out var gun))
        {
            return;
        }

        _gun.RefreshModifiers((held.HeldEntity.Value, gun));
    }

    private static string? HandName(BodyPartComponent part)
        => part.ParentSlot is { } slot ? SharedBodySystem.GetPartSlotContainerId(slot.Id) : null;
}
