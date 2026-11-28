using Content.Shared._Mono.Traits.Physical;
using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments.Combat;

public sealed partial class Wh40kAdrenalBoosterSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kAdrenalBoosterComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kAdrenalBoosterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kAdrenalBoosterComponent, KnockedDownEvent>(OnKnockedDown);
        SubscribeLocalEvent<LethargyComponent, ComponentInit>(OnLethargyInit);
        SubscribeLocalEvent<LethargyComponent, ComponentRemove>(OnLethargyRemove);
    }

    private void OnStartup(Entity<Wh40kAdrenalBoosterComponent> ent, ref ComponentStartup args)
        => SetStaminaBonus(ent, !HasComp<LethargyComponent>(ent));

    private void OnShutdown(Entity<Wh40kAdrenalBoosterComponent> ent, ref ComponentShutdown args)
        => SetStaminaBonus(ent, false);

    private void OnLethargyInit(Entity<LethargyComponent> ent, ref ComponentInit args)
    {
        if (TryComp<Wh40kAdrenalBoosterComponent>(ent, out var booster))
            SetStaminaBonus((ent, booster), false);
    }

    private void OnLethargyRemove(Entity<LethargyComponent> ent, ref ComponentRemove args)
    {
        if (TryComp<Wh40kAdrenalBoosterComponent>(ent, out var booster) && !TerminatingOrDeleted(ent))
            SetStaminaBonus((ent, booster), true);
    }

    private void SetStaminaBonus(Entity<Wh40kAdrenalBoosterComponent> ent, bool on)
    {
        if (ent.Comp.StaminaApplied == on || !TryComp<StaminaComponent>(ent, out var stamina))
            return;

        var sign = on ? 1f : -1f;
        stamina.CritThreshold += sign * ent.Comp.StaminaBonus;
        stamina.Decay += sign * ent.Comp.RegenerationBonus;
        stamina.Cooldown = on
            ? stamina.Cooldown * ent.Comp.CooldownMultiplier
            : stamina.Cooldown / ent.Comp.CooldownMultiplier;
        stamina.NextUpdate = _timing.CurTime;
        ent.Comp.StaminaApplied = on;
        Dirty(ent, stamina);
    }

    private void OnKnockedDown(Entity<Wh40kAdrenalBoosterComponent> ent, ref KnockedDownEvent args)
    {
        if (ent.Comp.SurgeAt != null ||
            _timing.CurTime < ent.Comp.ReadyAt ||
            !TryComp<StaminaComponent>(ent, out var stamina) ||
            (!stamina.Critical && stamina.StaminaDamage < stamina.CritThreshold))
        {
            return;
        }

        ent.Comp.SurgeAt = _timing.CurTime + ent.Comp.DownTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<Wh40kAdrenalBoosterComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.SurgeAt is { } surge && now >= surge)
            {
                comp.SurgeAt = null;
                Surge((uid, comp), now);
            }
            else if (comp.Spent && now >= comp.ReadyAt)
            {
                comp.Spent = false;
                _popup.PopupEntity(Loc.GetString("wh40k-adrenal-booster-ready"), uid, uid);
            }
        }
    }

    private void Surge(Entity<Wh40kAdrenalBoosterComponent> ent, TimeSpan now)
    {
        if (!_mobState.IsAlive(ent))
            return;

        if (TryComp<StaminaComponent>(ent, out var stamina) && stamina.StaminaDamage > 0f)
            _stamina.TakeStaminaDamage(ent, -stamina.StaminaDamage, stamina, visual: false, immediate: false);

        _status.TryRemoveStatusEffect(ent, "Stun");
        _status.TryRemoveStatusEffect(ent, "KnockedDown");

        ent.Comp.ReadyAt = now + ent.Comp.Cooldown;
        ent.Comp.Spent = true;
        _popup.PopupEntity(Loc.GetString("wh40k-adrenal-booster-surge"), ent, ent, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("wh40k-adrenal-booster-surge-others", ("target", ent.Owner)), ent,
            Filter.PvsExcept(ent), true);
    }
}
