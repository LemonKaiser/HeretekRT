using Content.Server.Atmos.Rotting;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Server.Particles;
using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Particles;
using Content.Shared.Popups;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments.Combat;

public sealed partial class Wh40kReviverSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EuiManager _euiManager = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private ParticleSpawnSystem _particles = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private RottingSystem _rotting = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kReviverComponent, EmpPulseEvent>(OnEmp);
    }

    private void OnEmp(Entity<Wh40kReviverComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        ent.Comp.NextShock = null;
        var until = _timing.CurTime + ent.Comp.EmpLockout;
        if (ent.Comp.ReadyAt < until)
            ent.Comp.ReadyAt = until;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        var query = EntityQueryEnumerator<Wh40kReviverComponent>();
        while (query.MoveNext(out var uid, out var reviver))
        {
            Tick((uid, reviver));
        }
    }

    public void Tick(Entity<Wh40kReviverComponent> ent)
    {
        var reviver = ent.Comp;
        var now = _timing.CurTime;

        if (!TryComp<MobStateComponent>(ent, out var mobState) || !_mobState.IsDead(ent, mobState))
        {
            if (reviver.ShocksGiven > 0 && reviver.ReadyAt < now + reviver.Recharge)
                reviver.ReadyAt = now + reviver.Recharge;

            reviver.NextShock = null;
            reviver.ShocksGiven = 0;
            reviver.Infused = 0f;
            return;
        }

        if (now < reviver.ReadyAt || _rotting.IsRotten(ent) || HasComp<UnrevivableComponent>(ent))
        {
            reviver.NextShock = null;
            return;
        }

        if (reviver.NextShock == null)
        {
            reviver.NextShock = now + (reviver.ShocksGiven == 0 ? reviver.ChargeTime : reviver.RetryDelay);
            if (reviver.ShocksGiven == 0)
            {
                _audio.PlayPvs(reviver.ChargeSound, ent);
                _popup.PopupEntity(Loc.GetString("wh40k-augment-reviver-charging"), ent, ent, PopupType.Medium);
            }
        }

        ArrestBleedingAndInfuse(ent);

        if (now < reviver.NextShock)
            return;

        reviver.NextShock = null;
        reviver.ShocksGiven++;

        _audio.PlayPvs(reviver.ZapSound, ent);
        _particles.Spawn(
            ent,
            "HrtDefibArc",
            parameters: new ParticleSpawnParameters(Intensity: 0.85f),
            attached: true,
            cooldown: TimeSpan.FromMilliseconds(400));
        _popup.PopupEntity(Loc.GetString("wh40k-augment-reviver-shock", ("target", Identity.Entity(ent, EntityManager))), ent, PopupType.MediumCaution);

        _damageable.TryChangeDamage(ent, reviver.ZapHeal, ignoreResistances: true, origin: ent);

        var revived = false;
        if (_mobThreshold.TryGetThresholdForState(ent, MobState.Dead, out var threshold) &&
            TryComp<DamageableComponent>(ent, out var damageable) &&
            damageable.TotalDamage < threshold)
        {
            _mobState.ChangeMobState(ent, MobState.Critical, mobState, ent);
            revived = true;

            if (_mind.TryGetMind(ent, out _, out var mind) &&
                _player.TryGetSessionById(mind.UserId, out var playerSession) &&
                mind.CurrentEntity != ent)
            {
                _euiManager.OpenEui(new ReturnToBodyEui(mind, _mind, _player), playerSession);
            }
        }

        _audio.PlayPvs(revived ? reviver.SuccessSound : reviver.FailureSound, ent);

        if (revived || reviver.ShocksGiven >= reviver.Shocks)
        {
            reviver.ShocksGiven = 0;
            reviver.Infused = 0f;
            reviver.ReadyAt = now + reviver.Recharge;
        }
    }

    private void ArrestBleedingAndInfuse(Entity<Wh40kReviverComponent> ent)
    {
        if (!TryComp<BloodstreamComponent>(ent, out var bloodstream))
            return;

        if (bloodstream.BleedAmount > 0f)
            _bloodstream.TryModifyBleedAmount(ent, -bloodstream.BleedAmount, bloodstream);

        var reviver = ent.Comp;
        if (reviver.Infused >= reviver.Reserve)
            return;

        var amount = MathF.Min(reviver.InfuseRate * (float) Interval.TotalSeconds, reviver.Reserve - reviver.Infused);
        if (amount > 0f && _bloodstream.TryModifyBloodLevel(ent, FixedPoint2.New(amount), bloodstream))
            reviver.Infused += amount;
    }
}
