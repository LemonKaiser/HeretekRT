using Content.Shared._WH40K.CharacterCreation;
using Content.Server._WH40K.Combat.PhantomStep;
using Content.Server._WH40K.ClassProgression;
using Content.Server._WH40K.Progression;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._WH40K.CharacterCreation;

/// <summary>
/// Applies the account-owned resolved stats to spawned and already living player mobs.
/// </summary>
public sealed class Wh40kCharacterStatsSpawnSystem : EntitySystem
{
    [Dependency] private MobThresholdSystem _mobThresholds = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private WH40KPhantomStepSystem _phantomStep = default!;
    [Dependency] private Wh40kProgressManager _progress = default!;
    [Dependency] private Wh40kCharacterStatResolver _resolver = default!;
    [Dependency] private SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<Wh40kCharacterStatsComponent, ComponentStartup>(OnStatsStartup);
        SubscribeLocalEvent<Wh40kCharacterStatsComponent, ComponentShutdown>(OnStatsShutdown);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!_progress.TryGetAccount(args.Player.UserId, out var account))
            return;

        ApplyAccountStats(args.Mob, account);
    }

    public void ApplyAccountStats(EntityUid uid, Wh40kAccountRpgRecord account)
    {
        if (TryComp<Wh40kClassRuntimeProfileComponent>(uid, out var classProfile))
        {
            ApplyResolvedStats(uid, _resolver.Resolve(
                account,
                classProfile.TalentModifiers,
                classProfile.EquipmentModifiers,
                classProfile.TemporaryModifiers));
            return;
        }

        ApplyResolvedStats(uid, _resolver.Resolve(account));
    }

    /// <summary>
    /// Restores the account-only baseline when a body-local class profile is detached.
    /// This deliberately ignores a profile that may still be shutting down on the entity.
    /// </summary>
    public void ApplyBaseAccountStats(EntityUid uid, Wh40kAccountRpgRecord account)
    {
        ApplyResolvedStats(uid, _resolver.Resolve(account));
    }

    internal void ApplyResolvedStats(EntityUid uid, Wh40kResolvedStats resolved)
    {
        var hadStats = TryComp<Wh40kCharacterStatsComponent>(uid, out var existingStats);
        var previousEnduranceEffect = hadStats
            ? GetEnduranceEffect(existingStats!.Endurance)
            : 0;
        var stats = EnsureComp<Wh40kCharacterStatsComponent>(uid);
        stats.Melee = resolved.GetFinal(Wh40kCharacteristic.Melee);
        stats.Ranged = resolved.GetFinal(Wh40kCharacteristic.Ranged);
        stats.Endurance = resolved.GetFinal(Wh40kCharacteristic.Endurance);
        stats.Intelligence = resolved.GetFinal(Wh40kCharacteristic.Intelligence);
        stats.Agility = resolved.GetFinal(Wh40kCharacteristic.Agility);
        Dirty(uid, stats);

        var dodgeCharges = GetPhantomStepCharges(stats.Agility);
        if (dodgeCharges > 0)
            _phantomStep.ConfigureForCharacter(uid, dodgeCharges);
        else
            RemComp<WH40KPhantomStepComponent>(uid);

        ApplyEnduranceDelta(uid, GetEnduranceEffect(stats.Endurance) - previousEnduranceEffect);
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
        RefreshHeldGunModifiers(uid);
    }

    private void OnStatsStartup(Entity<Wh40kCharacterStatsComponent> ent, ref ComponentStartup args)
    {
        ApplyEnduranceDelta(ent.Owner, GetEnduranceEffect(ent.Comp.Endurance));
    }

    private void OnStatsShutdown(Entity<Wh40kCharacterStatsComponent> ent, ref ComponentShutdown args)
    {
        ApplyEnduranceDelta(ent.Owner, -GetEnduranceEffect(ent.Comp.Endurance));
        _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);
        RefreshHeldGunModifiers(ent.Owner);
    }

    internal static int GetEnduranceEffect(int endurance)
    {
        return Wh40kCharacteristicEffects.GetEnduranceEffect(endurance);
    }

    internal static int GetPhantomStepCharges(int agility)
    {
        return Wh40kCharacteristicEffects.GetPhantomStepCharges(agility);
    }

    private void ApplyEnduranceDelta(EntityUid uid, int endurance)
    {
        if (endurance == 0)
            return;

        if (_mobThresholds.TryGetThresholdForState(uid, MobState.Critical, out var critical))
        {
            _mobThresholds.SetMobStateThreshold(
                uid,
                FixedPoint2.Max(0, critical.Value + endurance),
                MobState.Critical);
        }

        if (TryComp<StaminaComponent>(uid, out var stamina))
            ApplyStaminaEndurance(uid, stamina, endurance);
    }

    private void ApplyStaminaEndurance(EntityUid uid, StaminaComponent stamina, int endurance)
    {
        if (endurance == 0)
            return;

        stamina.CritThreshold = Math.Max(1f, stamina.CritThreshold + endurance);
        Dirty(uid, stamina);
    }

    private void RefreshHeldGunModifiers(EntityUid holder)
    {
        var query = EntityQueryEnumerator<GunComponent>();
        while (query.MoveNext(out var uid, out var gun))
        {
            if (gun.Holder == holder)
                _gun.RefreshModifiers((uid, gun), holder);
        }
    }
}
