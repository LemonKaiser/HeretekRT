using Content.Shared.Damage;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared.Durability;
using Content.Shared.Durability.Components;
using Content.Shared.Durability.Events;
using Content.Shared.Durability.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Durability;

/// <summary>
/// Server-authoritative durability state and consumption hooks.
/// </summary>
public sealed partial class ItemDurabilitySystem : SharedItemDurabilitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemDurabilityComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ItemDurabilityComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<ItemDurabilityComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<ItemDurabilityComponent, AttemptMeleeEvent>(OnMeleeAttempt);
        SubscribeLocalEvent<ItemDurabilityComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ItemDurabilityComponent, ToolUseAttemptEvent>(OnToolUseAttempt);
        SubscribeLocalEvent<ItemDurabilityComponent, ToolUseCompletedEvent>(OnToolUseCompleted);
        SubscribeLocalEvent<ItemDurabilityComponent, SurgeryToolUsedEvent>(OnSurgeryToolUsed);
        SubscribeLocalEvent<ItemDurabilityComponent, SurgeryToolUseCompletedEvent>(OnSurgeryToolUseCompleted);
        SubscribeLocalEvent<ItemDurabilityComponent, ArmorProtectionAppliedEvent>(OnArmorProtectionApplied);
        SubscribeLocalEvent<ItemDurabilityComponent, DamageChangedEvent>(OnSelfDamageChanged);
        SubscribeLocalEvent<ItemDurabilityComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ItemDurabilityComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ItemDurabilityComponent, RandomLootSpawnedEvent>(OnRandomLootSpawned);
    }

    public bool TryConsume(
        EntityUid uid,
        float amount,
        DurabilityReason reason,
        EntityUid? user = null,
        ItemDurabilityComponent? component = null)
    {
        if (!Resolve(uid, ref component, false) || component.Broken)
            return false;

        if (!float.IsFinite(amount) || amount <= 0f)
            return false;

        var oldValue = DurabilityMath.Round(component.CurrentDurability);
        if (!float.IsFinite(oldValue) || oldValue < 0f)
            oldValue = DurabilityMath.Round(component.MaxDurability);

        oldValue = DurabilityMath.Clamp(oldValue, component.MaxDurability);
        if (oldValue <= 0f)
        {
            Deplete(uid, component, reason, user);
            return false;
        }

        amount = DurabilityMath.Round(amount);
        var newValue = DurabilityMath.Round(MathF.Max(0f, oldValue - amount));
        newValue = DurabilityMath.Clamp(newValue, component.MaxDurability);
        var spent = DurabilityMath.Round(oldValue - newValue);
        component.CurrentDurability = newValue;
        Dirty(uid, component);

        RaiseLocalEvent(uid, new DurabilityChangedEvent(oldValue, newValue, spent, reason, user));

        if (newValue <= 0f)
            Deplete(uid, component, reason, user);

        return spent > 0f;
    }

    /// <summary>
    /// Restores durability on the server. Broken non-destructive items become usable again once they are repaired.
    /// </summary>
    public bool TryRepair(
        EntityUid uid,
        float amount,
        EntityUid? user = null,
        ItemDurabilityComponent? component = null)
    {
        if (!Resolve(uid, ref component, false) || !float.IsFinite(amount) || amount <= 0f)
            return false;

        var oldValue = DurabilityMath.Round(component.CurrentDurability);
        if (!float.IsFinite(oldValue) || oldValue < 0f)
            oldValue = 0f;

        oldValue = DurabilityMath.Clamp(oldValue, component.MaxDurability);
        if (oldValue >= component.MaxDurability)
            return false;

        amount = DurabilityMath.Round(amount);
        var newValue = DurabilityMath.Round(MathF.Min(component.MaxDurability, oldValue + amount));
        newValue = DurabilityMath.Clamp(newValue, component.MaxDurability);
        component.CurrentDurability = newValue;
        if (newValue > 0f)
            component.Broken = false;

        Dirty(uid, component);
        RaiseLocalEvent(uid, new DurabilityChangedEvent(oldValue, newValue, newValue - oldValue, DurabilityReason.Repair, user));
        return newValue > oldValue;
    }

    private void OnMapInit(Entity<ItemDurabilityComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.MaxDurability = DurabilityMath.Round(ent.Comp.MaxDurability);
        if (!float.IsFinite(ent.Comp.MaxDurability) || ent.Comp.MaxDurability <= 0f)
        {
            Log.Error($"Entity {ToPrettyString(ent)} has invalid maximum durability {ent.Comp.MaxDurability}.");
            RemCompDeferred<ItemDurabilityComponent>(ent);
            return;
        }

        if (!float.IsFinite(ent.Comp.CurrentDurability) || ent.Comp.CurrentDurability < 0f)
            ent.Comp.CurrentDurability = ent.Comp.MaxDurability;

        ent.Comp.CurrentDurability = DurabilityMath.Clamp(ent.Comp.CurrentDurability, ent.Comp.MaxDurability);
        Dirty(ent);

        if (ent.Comp.CurrentDurability <= 0f)
            Deplete(ent, ent.Comp, DurabilityReason.Initialization, null);

        RaiseLocalEvent(ent.Owner, new DurabilityInitializedEvent());
    }

    private void OnRandomLootSpawned(Entity<ItemDurabilityComponent> ent, ref RandomLootSpawnedEvent args)
    {
        var maximum = DurabilityMath.Round(ent.Comp.MaxDurability);
        if (!float.IsFinite(maximum) || maximum <= 0f)
            return;

        var minimum = MathF.Max(0.1f, maximum * 0.01f);
        var upper = MathF.Max(minimum, maximum * 0.5f);
        var current = DurabilityMath.Round(_random.NextFloat(minimum, upper));
        current = MathF.Max(minimum, current);

        ent.Comp.CurrentDurability = DurabilityMath.Clamp(current, maximum);
        ent.Comp.Broken = false;
        Dirty(ent);
    }

    private void OnShotAttempted(Entity<ItemDurabilityComponent> ent, ref ShotAttemptedEvent args)
    {
        if (IsUsable(ent.Owner, ent.Comp))
            return;

        args.Cancel();
        _popup.PopupClient(Loc.GetString("durability-item-broken"), ent.Owner, args.User);
    }

    private void OnGunShot(Entity<ItemDurabilityComponent> ent, ref GunShotEvent args)
    {
        if (ent.Comp.ShotDrain <= 0f || args.Ammo.Count == 0)
            return;

        TryConsume(ent, ent.Comp.ShotDrain * args.Ammo.Count, DurabilityReason.Shot, args.User);
    }

    private void OnMeleeAttempt(Entity<ItemDurabilityComponent> ent, ref AttemptMeleeEvent args)
    {
        if (IsUsable(ent.Owner, ent.Comp))
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString("durability-item-broken");
    }

    private void OnMeleeHit(Entity<ItemDurabilityComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0 || ent.Comp.MeleeDrain <= 0f)
            return;

        TryConsume(ent, ent.Comp.MeleeDrain, DurabilityReason.MeleeHit, args.User);
    }

    private void OnToolUseAttempt(Entity<ItemDurabilityComponent> ent, ref ToolUseAttemptEvent args)
    {
        if (IsUsable(ent.Owner, ent.Comp))
            return;

        args.Cancel();
        _popup.PopupClient(Loc.GetString("durability-item-broken"), ent.Owner, args.User);
    }

    private void OnToolUseCompleted(Entity<ItemDurabilityComponent> ent, ref ToolUseCompletedEvent args)
    {
        if (ent.Comp.ToolUseDrain <= 0f)
            return;

        TryConsume(ent, ent.Comp.ToolUseDrain, DurabilityReason.ToolUse, args.User);
    }

    private void OnSurgeryToolUsed(Entity<ItemDurabilityComponent> ent, ref SurgeryToolUsedEvent args)
    {
        if (IsUsable(ent.Owner, ent.Comp))
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString("durability-item-broken"), ent.Owner, args.User);
    }

    private void OnSurgeryToolUseCompleted(Entity<ItemDurabilityComponent> ent, ref SurgeryToolUseCompletedEvent args)
    {
        if (ent.Comp.ToolUseDrain <= 0f)
            return;

        TryConsume(ent, ent.Comp.ToolUseDrain, DurabilityReason.ToolUse, args.User);
    }

    private void OnArmorProtectionApplied(Entity<ItemDurabilityComponent> ent, ref ArmorProtectionAppliedEvent args)
    {
        if (ent.Comp.ArmorDamageDrainMultiplier <= 0f || !CoversBodyPart(ent.Comp, args.TargetPart))
            return;

        var drain = DurabilityMath.CalculateArmorDamageDrain(
            args.AbsorbedDamage,
            ent.Comp.ArmorDamageDrainMultiplier);
        TryConsume(ent, drain, DurabilityReason.IncomingDamage);
    }

    private void OnSelfDamageChanged(Entity<ItemDurabilityComponent> ent, ref DamageChangedEvent args)
    {
        if (!ent.Comp.DrainOnDamage || !args.DamageIncreased || ent.Comp.IncomingDamageDrain <= 0f)
            return;

        TryConsume(ent, ent.Comp.IncomingDamageDrain, DurabilityReason.IncomingDamage, args.Origin);
    }

    private void OnUseInHand(Entity<ItemDurabilityComponent> ent, ref UseInHandEvent args)
    {
        if (IsUsable(ent.Owner, ent.Comp))
            return;

        args.Handled = true;
        _popup.PopupClient(Loc.GetString("durability-item-broken"), ent.Owner, args.User);
    }

    private void OnExamined(Entity<ItemDurabilityComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var current = DurabilityMath.Format(DurabilityMath.Clamp(ent.Comp.CurrentDurability, ent.Comp.MaxDurability));
        var maximum = DurabilityMath.Format(ent.Comp.MaxDurability);
        args.PushMarkup(Loc.GetString("durability-examine", ("current", current), ("max", maximum)));

        if (ent.Comp.Broken)
            args.PushMarkup(Loc.GetString("durability-examine-broken"));
    }

    private void Deplete(
        EntityUid uid,
        ItemDurabilityComponent component,
        DurabilityReason reason,
        EntityUid? user)
    {
        if (component.Broken)
            return;

        component.CurrentDurability = 0f;
        component.Broken = true;
        Dirty(uid, component);
        RaiseLocalEvent(uid, new DurabilityDepletedEvent(reason, user));

        if (component.DestroyAtZero)
            QueueDel(uid);
    }
}
