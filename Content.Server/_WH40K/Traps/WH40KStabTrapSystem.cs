using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Throwing;
using Content.Shared._WH40K.Traps;
using Content.Server.Particles;
using Content.Shared.Particles;
using Content.Shared.Maps;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;

namespace Content.Server._WH40K.Traps;

/// <summary>
/// Runs the entire floor-spike cycle from the entity-system update loop.
/// </summary>
public sealed partial class WH40KStabTrapSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly ParticleSpawnSystem _particles = default!;

    private readonly HashSet<EntityUid> _strikeTargets = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KStabTrapComponent, ComponentStartup>(OnStartup);
    }

    public override void Update(float frameTime)
    {
        var enumerator = EntityQueryEnumerator<WH40KStabTrapComponent>();

        while (enumerator.MoveNext(out var uid, out var component))
        {
            var entered = UpdateOccupants(uid, component);
            if (component.Phase == WH40KStabTrapPhase.Ready && entered)
                Activate(uid, component);

            if (component.Phase == WH40KStabTrapPhase.Ready)
                continue;

            component.PhaseTimeRemaining -= frameTime;
            if (component.PhaseTimeRemaining > 0)
                continue;

            switch (component.Phase)
            {
                case WH40KStabTrapPhase.Activating:
                    Strike(uid, component);
                    SetPhase(uid, component, WH40KStabTrapPhase.Extended, component.ExtendedDuration);
                    break;

                case WH40KStabTrapPhase.Extended:
                    _audio.PlayPvs(component.RetractSound, uid);
                    SetPhase(uid, component, WH40KStabTrapPhase.Retracting, component.RetractionDuration);
                    break;

                case WH40KStabTrapPhase.Retracting:
                {
                    // Cooldown starts with the retracting animation. Its remaining portion starts
                    // only after the visual returns to idle, so the full cooldown is five seconds.
                    var remainingCooldown = component.RechargeDelay - component.RetractionDuration;
                    SetPhase(uid, component, WH40KStabTrapPhase.Recharging, remainingCooldown > 0f ? remainingCooldown : 0f);
                    break;
                }

                case WH40KStabTrapPhase.Recharging:
                    component.Phase = WH40KStabTrapPhase.Ready;
                    component.PhaseTimeRemaining = 0;
                    break;
            }
        }
    }

    private void OnStartup(Entity<WH40KStabTrapComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.Phase = WH40KStabTrapPhase.Ready;
        entity.Comp.PhaseTimeRemaining = 0;
        entity.Comp.TileWasOccupied = IsTileOccupied(entity.Owner, entity.Comp.QueriedOccupants);
        entity.Comp.StrikeResolvedThisCycle = false;
        SetVisualState(entity.Owner, WH40KStabTrapVisualState.Idle);
    }

    private void Activate(EntityUid uid, WH40KStabTrapComponent component)
    {
        component.StrikeResolvedThisCycle = false;
        SetPhase(uid, component, WH40KStabTrapPhase.Activating, component.ActivationDelay);
        _audio.PlayPvs(component.ExtendSound, uid);
    }

    private bool UpdateOccupants(EntityUid uid, WH40KStabTrapComponent component)
    {
        var tileIsOccupied = IsTileOccupied(uid, component.QueriedOccupants);
        var entered = tileIsOccupied && !component.TileWasOccupied;
        component.TileWasOccupied = tileIsOccupied;
        return entered;
    }

    private bool IsTileOccupied(EntityUid uid, HashSet<EntityUid> occupants)
    {
        occupants.Clear();

        if (!_turf.TryGetTileRef(Transform(uid).Coordinates, out var tileRef))
            return false;

        _lookup.GetLocalEntitiesIntersecting(
            tileRef.Value.GridUid,
            tileRef.Value.GridIndices,
            occupants,
            flags: LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries);

        foreach (var target in occupants)
        {
            if (target != uid && IsTrigger(target))
                return true;
        }

        return false;
    }

    private void Strike(EntityUid uid, WH40KStabTrapComponent component)
    {
        // The damage instant is a one-shot per cycle. It remains guarded even if
        // a malformed phase transition attempts to invoke it a second time.
        if (component.StrikeResolvedThisCycle)
            return;

        component.StrikeResolvedThisCycle = true;

        // The damage affects everyone still standing on the trap's tile, rather than
        // depending on the trigger sensor's physics shape.
        if (_turf.TryGetTileRef(Transform(uid).Coordinates, out var tileRef))
        {
            _strikeTargets.Clear();
            _lookup.GetLocalEntitiesIntersecting(
                tileRef.Value.GridUid,
                tileRef.Value.GridIndices,
                _strikeTargets,
                flags: LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries);

            var dealtDamage = false;
            foreach (var target in _strikeTargets)
            {
                if (target == uid || !IsLivingMob(target) || !HasComp<DamageableComponent>(target))
                    continue;

                var damageResult = _damageable.TryChangeDamage(
                    target,
                    new DamageSpecifier(component.Damage),
                    origin: uid);
                dealtDamage |= damageResult is { Empty: false };
            }

            if (dealtDamage)
            {
                _audio.PlayPvs(component.StrikeSound, uid);
                _particles.Spawn(
                    uid,
                    "HrtMetalChips",
                    parameters: new ParticleSpawnParameters(Intensity: 0.7f),
                    cooldown: TimeSpan.FromMilliseconds(350));
            }
        }
    }

    private void SetPhase(
        EntityUid uid,
        WH40KStabTrapComponent component,
        WH40KStabTrapPhase phase,
        float duration)
    {
        component.Phase = phase;
        component.PhaseTimeRemaining = duration;
        switch (phase)
        {
            case WH40KStabTrapPhase.Activating:
                SetVisualState(uid, WH40KStabTrapVisualState.Activating);
                break;
            case WH40KStabTrapPhase.Recharging:
                SetVisualState(uid, WH40KStabTrapVisualState.Idle);
                break;
        }
    }

    private void SetVisualState(EntityUid uid, WH40KStabTrapVisualState state)
    {
        _appearance.SetData(uid, WH40KStabTrapVisuals.State, state);
    }

    /// <summary>
    /// Living and critical mobs are valid trap victims. Dead bodies and arbitrary
    /// damageable structures are deliberately excluded.
    /// </summary>
    private bool IsLivingMob(EntityUid uid)
    {
        return TryComp<MobStateComponent>(uid, out var mobState)
               && mobState.CurrentState != MobState.Dead;
    }

    /// <summary>
    /// A trap reacts to living mobs and to objects that are actively being thrown.
    /// Stationary items, structures, and corpses cannot trigger it.
    /// </summary>
    private bool IsTrigger(EntityUid uid)
    {
        return IsLivingMob(uid)
               || TryComp<ThrownItemComponent>(uid, out var thrown) && !thrown.Landed;
    }

}
