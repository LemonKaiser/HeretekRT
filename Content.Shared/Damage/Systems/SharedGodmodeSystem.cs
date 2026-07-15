using System.Linq;
using Content.Shared.Damage.Components;
using Content.Shared.Rejuvenate;
using Content.Shared.Slippery;
using Content.Shared.StatusEffect;
using Content.Shared.Body.Systems; // Shitmed Change
using Robust.Shared.GameObjects;

namespace Content.Shared.Damage.Systems;

public abstract partial class SharedGodmodeSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;

    [Dependency] private SharedBodySystem _bodySystem = default!; // Shitmed Change

    // Rejuvenate handlers may depend on containers and other components that are created
    // later in the entity lifecycle. Keep early Godmode applications pending until the
    // entity has finished initialization.
    private readonly HashSet<EntityUid> _pendingRejuvenation = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GodmodeComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<GodmodeComponent, BeforeStatusEffectAddedEvent>(OnBeforeStatusEffect);
        SubscribeLocalEvent<GodmodeComponent, BeforeStaminaDamageEvent>(OnBeforeStaminaDamage);
        SubscribeLocalEvent<GodmodeComponent, SlipAttemptEvent>(OnSlipAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingRejuvenation.Count == 0)
            return;

        var pending = _pendingRejuvenation.ToArray();
        _pendingRejuvenation.Clear();

        foreach (var uid in pending)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (!TryComp<MetaDataComponent>(uid, out var metadata))
                continue;

            // EntityLifeStage remains Initialized while ComponentStartup is running,
            // but by the next system tick that synchronous startup pass is complete.
            // Only keep retrying entities that are still being initialized.
            if (metadata.EntityLifeStage <= EntityLifeStage.Initializing)
            {
                _pendingRejuvenation.Add(uid);
                continue;
            }

            if (HasComp<GodmodeComponent>(uid))
                RejuvenateGodmodeEntity(uid);
        }
    }

    private void OnSlipAttempt(EntityUid uid, GodmodeComponent component, SlipAttemptEvent args)
    {
        args.NoSlip = true;
    }

    private void OnBeforeDamageChanged(EntityUid uid, GodmodeComponent component, ref BeforeDamageChangedEvent args)
    {
        args.Cancelled = true;
    }

    private void OnBeforeStatusEffect(EntityUid uid, GodmodeComponent component, ref BeforeStatusEffectAddedEvent args)
    {
        args.Cancelled = true;
    }

    private void OnBeforeStaminaDamage(EntityUid uid, GodmodeComponent component, ref BeforeStaminaDamageEvent args)
    {
        args.Cancelled = true;
    }

    public virtual void EnableGodmode(EntityUid uid, GodmodeComponent? godmode = null)
    {
        godmode ??= EnsureComp<GodmodeComponent>(uid);

        if (TryComp<DamageableComponent>(uid, out var damageable))
        {
            godmode.OldDamage = new DamageSpecifier(damageable.Damage);
        }

        // Rejuv to cover other stuff. Do not raise it while the entity is still being
        // initialized: handlers may rely on containers created by ComponentStartup.
        if (!TryComp<MetaDataComponent>(uid, out var metadata))
            return;

        if (metadata.EntityLifeStage <= EntityLifeStage.Initialized)
        {
            _pendingRejuvenation.Add(uid);
            return;
        }

        RejuvenateGodmodeEntity(uid);
    }

    private void RejuvenateGodmodeEntity(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || !HasComp<GodmodeComponent>(uid))
            return;

        RaiseLocalEvent(uid, new RejuvenateEvent());

        foreach (var (id, _) in _bodySystem.GetBodyChildren(uid)) // Shitmed Change
            EnableGodmode(id);
    }

    public virtual void DisableGodmode(EntityUid uid, GodmodeComponent? godmode = null)
    {
        if (!Resolve(uid, ref godmode, false))
            return;

        if (TryComp<DamageableComponent>(uid, out var damageable) && godmode.OldDamage != null)
        {
            _damageable.SetDamage(uid, damageable, godmode.OldDamage);
        }

        RemComp<GodmodeComponent>(uid);

        foreach (var (id, _) in _bodySystem.GetBodyChildren(uid)) // Shitmed Change
            DisableGodmode(id);
    }

    /// <summary>
    ///     Toggles godmode for a given entity.
    /// </summary>
    /// <param name="uid">The entity to toggle godmode for.</param>
    /// <returns>true if enabled, false if disabled.</returns>
    public bool ToggleGodmode(EntityUid uid)
    {
        if (TryComp<GodmodeComponent>(uid, out var godmode))
        {
            DisableGodmode(uid, godmode);
            return false;
        }

        EnableGodmode(uid, godmode);
        return true;
    }
}
