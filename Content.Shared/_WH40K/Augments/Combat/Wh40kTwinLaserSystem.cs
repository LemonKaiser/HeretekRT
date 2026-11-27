using System.Linq;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._WH40K.Augments.Combat;

public sealed class Wh40kTwinLaserSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    private bool _twinning;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kTwinLaserComponent, GotEquippedHandEvent>(OnEquipped);
        SubscribeLocalEvent<Wh40kTwinLaserComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<Wh40kTwinLaserComponent, GunRefreshModifiersEvent>(OnRefresh);
        SubscribeLocalEvent<Wh40kTwinLaserComponent, GunShotEvent>(OnShot);
    }

    private void OnEquipped(Entity<Wh40kTwinLaserComponent> ent, ref GotEquippedHandEvent args)
        => RefreshTwins(args.User, ent);

    private void OnUnequipped(Entity<Wh40kTwinLaserComponent> ent, ref GotUnequippedHandEvent args)
        => RefreshTwins(args.User, ent);

    private void RefreshTwins(EntityUid user, EntityUid laser)
    {
        foreach (var twin in Twins(user, laser))
            _gun.RefreshModifiers(twin);
    }

    private void OnRefresh(Entity<Wh40kTwinLaserComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (args.Gun.Comp.Holder is not { } user || !Twins(user, ent).Any())
            return;

        args.MinAngle *= ent.Comp.Spread;
        args.MaxAngle *= ent.Comp.Spread;
        args.AngleIncrease *= ent.Comp.Spread;
    }

    private void OnShot(Entity<Wh40kTwinLaserComponent> ent, ref GunShotEvent args)
    {
        if (_twinning)
            return;

        _twinning = true;
        try
        {
            foreach (var twin in Twins(args.User, ent))
            {
                if (TryComp<GunComponent>(twin, out var gun))
                    _gun.AttemptShoot(args.User, twin, gun, args.ToCoordinates);
            }
        }
        finally
        {
            _twinning = false;
        }
    }

    private IEnumerable<EntityUid> Twins(EntityUid user, EntityUid laser)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held != laser && HasComp<Wh40kTwinLaserComponent>(held))
                yield return held;
        }
    }
}
