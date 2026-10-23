using Content.Shared._Mono.Blocking;
using Content.Shared.Damage;
using Content.Shared.Durability.Events;
using Content.Shared.Durability.Systems;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Content.Shared.Blocking.Components;
using Content.Shared.Inventory;

namespace Content.Shared.Blocking;

public sealed partial class BlockingSystem : SharedBlockingSystem // Mono
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedItemDurabilitySystem _durability = default!;

    private void InitializeUser()
    {
        SubscribeLocalEvent<BlockingUserComponent, DamageModifyEvent>(OnUserDamageModified, before: [typeof(InventorySystem)]);
        SubscribeLocalEvent<BlockingComponent, DamageModifyEvent>(OnDamageModified, before: [typeof(InventorySystem)]);
        SubscribeLocalEvent<BlockingComponent, DurabilityDepletedEvent>(OnDurabilityDepleted);

        SubscribeLocalEvent<BlockingUserComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<BlockingUserComponent, ContainerGettingInsertedAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<BlockingUserComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<BlockingUserComponent, EntityTerminatingEvent>(OnEntityTerminating);
    }

    private void OnParentChanged(EntityUid uid, BlockingUserComponent component, ref EntParentChangedMessage args)
    {
        UserStopBlocking(uid, component);
    }

    private void OnInsertAttempt(EntityUid uid, BlockingUserComponent component, ContainerGettingInsertedAttemptEvent args)
    {
        UserStopBlocking(uid, component);
    }

    private void OnAnchorChanged(EntityUid uid, BlockingUserComponent component, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            return;

        UserStopBlocking(uid, component);
    }

    private void OnUserDamageModified(EntityUid uid, BlockingUserComponent component, DamageModifyEvent args)
    {
        ApplyBlocking(uid, component, args, damageShield: true);
    }

    public void ApplyPartBlocking(EntityUid body, DamageModifyEvent args)
    {
        if (TryComp<BlockingUserComponent>(body, out var blocking))
            ApplyBlocking(body, blocking, args, damageShield: false);
    }

    private void ApplyBlocking(EntityUid uid, BlockingUserComponent component, DamageModifyEvent args, bool damageShield)
    {
        if (component.BlockingItem is not { } item || !_durability.IsUsable(item))
            return;

        if (TryComp<BlockingComponent>(component.BlockingItem, out var blocking)) // Mono
        {
            if (args.Damage.GetTotal() <= 0)
                return;

            // A shield should only block damage it can itself absorb. To determine that we need the Damageable component on it.
            if (!TryComp<DamageableComponent>(component.BlockingItem, out var dmgComp))
                return;

            if (TryComp<ItemToggleComponent>(component.BlockingItem, out var toggleComponent) && !toggleComponent.Activated) // Mono
                return;

            var blockFraction = blocking.IsBlocking ? blocking.ActiveBlockFraction : blocking.PassiveBlockFraction;
            blockFraction = Math.Clamp(blockFraction, 0, 1);
            // Part damage is resolved before the owning body. Only the body's hit spends shield durability.
            if (damageShield)
                _damageable.TryChangeDamage(component.BlockingItem,
                    blockFraction * args.OriginalDamage,
                    armorPenetration: args.ArmorPenetration); // Goob edit

            var modify = new DamageModifierSet();
            foreach (var key in dmgComp.Damage.DamageDict.Keys)
            {
                modify.Coefficients.TryAdd(key, 1 - blockFraction);
            }

            args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, modify);

            if (damageShield && blocking.IsBlocking && !args.Damage.Equals(args.OriginalDamage))
            {
                _audio.PlayPvs(blocking.BlockSound, uid);
            }
        }
    }

    private void OnDamageModified(EntityUid uid, BlockingComponent component, DamageModifyEvent args)
    {
        var modifier = component.IsBlocking ? component.ActiveBlockDamageModifier : component.PassiveBlockDamageModifer;
        if (modifier == null)
        {
            return;
        }

        args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, modifier);
    }

    private void OnDurabilityDepleted(EntityUid uid, BlockingComponent component, ref DurabilityDepletedEvent args)
    {
        if (component.IsBlocking && component.User is { } user)
            StopBlocking(uid, component, user);
    }

    private void OnEntityTerminating(EntityUid uid, BlockingUserComponent component, ref EntityTerminatingEvent args)
    {
        if (!TryComp<BlockingComponent>(component.BlockingItem, out var blockingComponent))
            return;

        StopBlockingHelper(component.BlockingItem.Value, blockingComponent, uid);

    }

    /// <summary>
    /// Check for the shield and has the user stop blocking
    /// Used where you'd like the user to stop blocking, but also don't want to remove the <see cref="BlockingUserComponent"/>
    /// </summary>
    /// <param name="uid">The user blocking</param>
    /// <param name="component">The <see cref="BlockingUserComponent"/></param>
    private void UserStopBlocking(EntityUid uid, BlockingUserComponent component)
    {
        if (TryComp<BlockingComponent>(component.BlockingItem, out var blockComp) && blockComp.IsBlocking)
            StopBlocking(component.BlockingItem.Value, blockComp, uid);
    }
}
