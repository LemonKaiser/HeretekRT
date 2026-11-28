using System.Diagnostics.CodeAnalysis;
using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Actions;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Augments.Combat;

public sealed partial class Wh40kHandShieldSystem : SharedWh40kHandShieldSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private Wh40kDeployableImplantSystem _deployables = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kHandShieldComponent, Wh40kDeployAttemptEvent>(OnDeployAttempt);
        SubscribeLocalEvent<Wh40kHandShieldComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<Wh40kHandShieldUserComponent, HitScanReflectAttemptEvent>(OnHitscan);
        SubscribeLocalEvent<Wh40kHandShieldUserComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
        SubscribeLocalEvent<Wh40kHandShieldUserComponent, BeforeStaminaDamageEvent>(OnBeforeStamina);
    }

    private void OnRemove(Entity<Wh40kHandShieldComponent> ent, ref ComponentRemove args)
    {
        Lower(ent.Owner, ent.Comp);
    }

    private void OnDeployAttempt(Entity<Wh40kHandShieldComponent> ent, ref Wh40kDeployAttemptEvent args)
    {
        if (ent.Comp.Collapsed)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-hand-shield-recharging"), args.Body, args.Body);
            args.Cancelled = true;
        }
        else if (TryComp<Wh40kHandShieldUserComponent>(args.Body, out var user) && user.Implant != ent.Owner)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-hand-shield-other"), args.Body, args.Body);
            args.Cancelled = true;
        }
    }

    protected override void ProjectileBlocked(Entity<Wh40kHandShieldUserComponent> ent, Wh40kHandShieldComponent shield,
        EntityUid projectile, float damage)
    {
        Soak(ent, shield, damage, true);
        QueueDel(projectile);
    }

    private void OnHitscan(Entity<Wh40kHandShieldUserComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected || !Covers(ent, -args.Direction, out var shield))
            return;

        Soak(ent, shield, args.Damage?.GetTotal().Float() ?? 0f, true);
        var spread = _random.NextAngle(-shield.DeflectSpread / 2, shield.DeflectSpread / 2);
        args.Direction = -spread.RotateVec(args.Direction);
        args.Reflected = true;
    }

    private void OnBeforeDamage(Entity<Wh40kHandShieldUserComponent> ent, ref BeforeDamageChangedEvent args)
    {
        var damage = args.Damage.GetTotal().Float();
        if (!args.Cancelled && damage > 0 && TryBlockBlow(ent, args.Origin, args.Tool, damage))
            args.Cancelled = true;
    }

    private void OnBeforeStamina(Entity<Wh40kHandShieldUserComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        if (!args.Cancelled && args.Value > 0 && TryBlockBlow(ent, args.Source, args.With, args.Value))
            args.Cancelled = true;
    }

    private bool TryBlockBlow(Entity<Wh40kHandShieldUserComponent> ent, EntityUid? origin, EntityUid? tool, float amount)
    {
        if (!IsBlow(ent, origin, tool, out var attacker))
            return false;

        var from = _transform.GetWorldPosition(attacker.Value) - _transform.GetWorldPosition(ent);
        if (!Covers(ent, from, out var shield))
            return false;

        Soak(ent, shield, amount, false);
        return true;
    }

    private bool IsBlow(EntityUid body, EntityUid? origin, EntityUid? tool, [NotNullWhen(true)] out EntityUid? attacker)
    {
        attacker = origin;
        return origin is { } who &&
               who != body &&
               tool is { } with &&
               HasComp<MeleeWeaponComponent>(with) &&
               !HasComp<ProjectileComponent>(with);
    }

    private void Soak(Entity<Wh40kHandShieldUserComponent> ent, Wh40kHandShieldComponent shield, float amount, bool shot)
    {
        if (shield.LastBlockSound != _timing.CurTime)
        {
            shield.LastBlockSound = _timing.CurTime;
            _audio.PlayPvs(shot ? shield.ShotSound : shield.BlowSound, ent, AudioParams.Default.WithVariation(0.05f));
        }

        shield.Charge -= amount;
        if (shield.Charge <= 0)
            Collapse(ent.Comp.Implant, shield, ent);
    }

    private void Collapse(EntityUid implantUid, Wh40kHandShieldComponent shield, EntityUid body)
    {
        shield.Charge = 0;
        shield.Collapsed = true;
        Dirty(implantUid, shield);
        _deployables.Stow(implantUid, shield);
        Lower(implantUid, shield);
        _audio.PlayPvs(shield.CollapseSound, body);
        _popup.PopupEntity(Loc.GetString("wh40k-hand-shield-collapse"), body, body, PopupType.MediumCaution);
        if (shield.ActionEntity is { } action)
            _actions.SetCooldown(action, TimeSpan.FromSeconds(shield.MaxCharge / shield.RechargeRate));
    }

    private void Raise(EntityUid implantUid, Wh40kHandShieldComponent shield, EntityUid body)
    {
        var user = EnsureComp<Wh40kHandShieldUserComponent>(body);
        user.Implant = implantUid;
        Dirty(body, user);
        shield.RaisedBy = body;
    }

    private void Lower(EntityUid implantUid, Wh40kHandShieldComponent shield)
    {
        if (shield.RaisedBy is { } body &&
            !TerminatingOrDeleted(body) &&
            TryComp<Wh40kHandShieldUserComponent>(body, out var user) &&
            user.Implant == implantUid)
        {
            RemCompDeferred(body, user);
        }

        shield.RaisedBy = null;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<Wh40kHandShieldComponent>();
        while (query.MoveNext(out var uid, out var shield))
        {
            var ownerBody = CompOrNull<OrganComponent>(uid)?.Body ?? CompOrNull<BodyPartComponent>(uid)?.Body;
            var raisedBy = Exists(shield.Deployed) ? ownerBody : null;
            if (raisedBy != shield.RaisedBy)
            {
                Lower(uid, shield);
                if (raisedBy is { } body)
                    Raise(uid, shield, body);
            }

            if (raisedBy != null || shield.Charge >= shield.MaxCharge)
                continue;

            shield.Charge = Math.Min(shield.MaxCharge, shield.Charge + shield.RechargeRate * frameTime);
            if (shield.Charge >= shield.MaxCharge && shield.Collapsed)
            {
                shield.Collapsed = false;
                Dirty(uid, shield);
            }
        }
    }
}
