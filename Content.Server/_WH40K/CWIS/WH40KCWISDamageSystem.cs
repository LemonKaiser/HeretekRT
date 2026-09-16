using Content.Shared._WH40K.CWIS;
using Content.Shared.Damage;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.CWIS;

public sealed class WH40KCWISDamageSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KCWISDamageComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KCWISDamageComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WH40KCWISDamageComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnMapInit(Entity<WH40KCWISDamageComponent> entity, ref MapInitEvent args)
    {
        if (TryComp<DamageableComponent>(entity, out var damageable))
            UpdateState(entity, damageable, true);
    }

    private void OnDamageChanged(Entity<WH40KCWISDamageComponent> entity, ref DamageChangedEvent args)
    {
        UpdateState(entity, args.Damageable);
    }

    private void OnShotAttempted(Entity<WH40KCWISDamageComponent> entity, ref ShotAttemptedEvent args)
    {
        if (entity.Comp.State == WH40KCWISDamageState.Destroyed)
            args.Cancel();
    }

    private void UpdateState(Entity<WH40KCWISDamageComponent> entity, DamageableComponent damageable, bool force = false)
    {
        if (entity.Comp.DamageThreshold <= 0f)
            return;

        var damageFraction = damageable.TotalDamage.Float() / entity.Comp.DamageThreshold;
        var state = damageFraction switch
        {
            >= 1f => WH40KCWISDamageState.Destroyed,
            >= 0.75f => WH40KCWISDamageState.HeavilyDamaged,
            >= 0.25f => WH40KCWISDamageState.Damaged,
            _ => WH40KCWISDamageState.Intact
        };

        if (!force && state == entity.Comp.State)
            return;

        entity.Comp.State = state;
        _appearance.SetData(entity, WH40KCWISVisuals.DamageState, state);
    }
}
