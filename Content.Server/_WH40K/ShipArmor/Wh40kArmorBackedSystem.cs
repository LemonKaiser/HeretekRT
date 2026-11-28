using System.Linq;
using Content.Server.Destructible;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.ArmorPiercing;
using Content.Shared._WH40K.ShipArmor;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Hitscan.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.ShipArmor;

/// <summary>
/// Transfers a thickness-scaled share of blast, projectile, and hitscan hits on thrusters and ship hardpoints
/// onto adjacent destructible hull armor, and shields their floor tile until backing armor falls.
/// </summary>
public sealed partial class Wh40kArmorBackedSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private const float FootprintMargin = 0.95f;

    private EntityQuery<ArmorThicknessComponent> _armorQuery;
    private EntityQuery<DamageableComponent> _damageableQuery;
    private EntityQuery<DestructibleComponent> _destructibleQuery;
    private EntityQuery<ProjectileComponent> _projectileQuery;
    private EntityQuery<HitscanBasicDamageComponent> _hitscanQuery;
    private bool _redirecting;

    private float _transfer;
    private float _fullThickness;

    public override void Initialize()
    {
        base.Initialize();
        _armorQuery = GetEntityQuery<ArmorThicknessComponent>();
        _damageableQuery = GetEntityQuery<DamageableComponent>();
        _destructibleQuery = GetEntityQuery<DestructibleComponent>();
        _projectileQuery = GetEntityQuery<ProjectileComponent>();
        _hitscanQuery = GetEntityQuery<HitscanBasicDamageComponent>();

        Subs.CVar(_cfg, Wh40kShipArmorCVars.Transfer, v => _transfer = v, true);
        Subs.CVar(_cfg, Wh40kShipArmorCVars.FullThickness, v => _fullThickness = v, true);

        SubscribeLocalEvent<ThrusterComponent, BeforeDamageChangedEvent>(
            (EntityUid uid, ThrusterComponent _, ref BeforeDamageChangedEvent args) => OnBeforeDamage(uid, ref args));
        SubscribeLocalEvent<HardpointAnchorableOnlyComponent, BeforeDamageChangedEvent>(
            (EntityUid uid, HardpointAnchorableOnlyComponent _, ref BeforeDamageChangedEvent args) => OnBeforeDamage(uid, ref args));
        SubscribeLocalEvent<HardpointComponent, BeforeDamageChangedEvent>(
            (EntityUid uid, HardpointComponent _, ref BeforeDamageChangedEvent args) => OnBeforeDamage(uid, ref args));

        SubscribeLocalEvent<ThrusterComponent, GetVerbsEvent<ExamineVerb>>(
            (EntityUid uid, ThrusterComponent comp, GetVerbsEvent<ExamineVerb> args) => OnCoverageVerb(uid, comp, args));
        SubscribeLocalEvent<HardpointAnchorableOnlyComponent, GetVerbsEvent<ExamineVerb>>(
            (EntityUid uid, HardpointAnchorableOnlyComponent comp, GetVerbsEvent<ExamineVerb> args) => OnCoverageVerb(uid, comp, args));
    }

    private void OnBeforeDamage(EntityUid uid, ref BeforeDamageChangedEvent args)
    {
        if (_redirecting || args.Cancelled)
            return;

        if (args.Damage.DamageDict.Values.Any(v => v < FixedPoint2.Zero))
            return;

        var transfer = GetTransfer(args);
        if (transfer <= 0f)
            return;

        if (FindBacking(uid) is not { } backing)
            return;

        var share = transfer * Coverage(backing.Comp.Thickness);
        if (share <= 0f)
            return;

        var ignoreGlobal = args.OriginFlag == DamageableSystem.DamageOriginFlag.Explosion;

        _redirecting = true;
        try
        {
            _damageable.TryChangeDamage(
                backing,
                args.Damage * share,
                args.IgnoreResistances,
                interruptsDoAfters: false,
                origin: args.Origin,
                ignoreGlobalModifiers: ignoreGlobal,
                tool: args.Tool,
                originFlag: args.OriginFlag);

            args.Applied = _damageable.TryChangeDamage(
                uid,
                args.Damage * (1f - share),
                args.IgnoreResistances,
                args.InterruptsDoAfters,
                origin: args.Origin,
                ignoreGlobalModifiers: ignoreGlobal,
                armorPenetration: args.ArmorPenetration,
                partMultiplier: args.PartMultiplier,
                targetPart: args.TargetPart,
                tool: args.Tool,
                originFlag: args.OriginFlag);
        }
        finally
        {
            _redirecting = false;
        }

        args.Cancelled = true;
    }

    private float GetTransfer(BeforeDamageChangedEvent args)
    {
        if (args.OriginFlag == DamageableSystem.DamageOriginFlag.Explosion)
            return _transfer;

        if (args.Tool is { } tool && (_projectileQuery.HasComp(tool) || _hitscanQuery.HasComp(tool)))
            return _transfer;

        return 0f;
    }

    private float Coverage(int thickness)
    {
        return Math.Clamp(thickness / _fullThickness, 0f, 1f);
    }

    private Entity<ArmorThicknessComponent>? FindBacking(EntityUid uid)
    {
        var xform = Transform(uid);
        if (!xform.Anchored || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var footprint = GetFootprint(uid, gridUid, xform);
        Entity<ArmorThicknessComponent>? best = null;
        var bestDamage = FixedPoint2.Zero;

        foreach (var maybeUid in _map.GetLocalAnchoredEntities(gridUid, grid, footprint))
        {
            if (maybeUid == uid || IsMount(maybeUid) || EntityManager.IsQueuedForDeletion(maybeUid))
                continue;

            if (!_armorQuery.TryComp(maybeUid, out var armor) ||
                !_damageableQuery.TryComp(maybeUid, out var damageable) ||
                !_destructibleQuery.HasComp(maybeUid))
                continue;

            var total = damageable.TotalDamage;
            if (best is not { } current ||
                armor.Thickness > current.Comp.Thickness ||
                (armor.Thickness == current.Comp.Thickness && total < bestDamage))
            {
                best = new Entity<ArmorThicknessComponent>(maybeUid, armor);
                bestDamage = total;
            }
        }

        return best;
    }

    private Box2 GetFootprint(EntityUid uid, EntityUid gridUid, TransformComponent xform)
    {
        var source = uid;
        var sourceXform = xform;
        if (TryComp<HardpointComponent>(uid, out var hardpoint) && hardpoint.anchoring is { } gun &&
            TryComp(gun, out TransformComponent? gunXform) && gunXform.ParentUid == gridUid)
        {
            source = gun;
            sourceXform = gunXform;
        }

        var pos = sourceXform.LocalPosition;
        var transform = new Transform(pos, (float) sourceXform.LocalRotation.Theta);
        var local = default(Box2);
        var any = false;

        if (TryComp<FixturesComponent>(source, out var fixtures))
        {
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard)
                    continue;

                var aabb = fixture.Shape.ComputeAABB(transform, 0);
                local = any ? local.Union(aabb) : aabb;
                any = true;
            }
        }

        if (!any)
            local = new Box2(pos, pos);

        return local.Enlarged(FootprintMargin);
    }

    public bool HoldsFloor(EntityUid uid)
    {
        return IsMount(uid) && FindBacking(uid) is not null;
    }

    private bool IsMount(EntityUid uid)
    {
        return HasComp<ThrusterComponent>(uid) ||
               HasComp<HardpointComponent>(uid) ||
               HasComp<HardpointAnchorableOnlyComponent>(uid);
    }

    private void OnCoverageVerb(EntityUid uid, Component comp, GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        _examine.AddDetailedExamineVerb(
            args,
            comp,
            GetCoverageMessage(uid),
            Loc.GetString("wh40k-armor-backed-verb-text"),
            "/Textures/_WH40K/ShipArmor/shield.svg.192dpi.png",
            Loc.GetString("wh40k-armor-backed-verb-message"));
    }

    private FormattedMessage GetCoverageMessage(EntityUid uid)
    {
        var msg = new FormattedMessage();
        if (FindBacking(uid) is not { } backing)
        {
            msg.AddMarkupOrThrow(Loc.GetString("wh40k-armor-backed-examine-none"));
            return msg;
        }

        var coverage = Coverage(backing.Comp.Thickness);
        msg.AddMarkupOrThrow(Loc.GetString("wh40k-armor-backed-examine",
            ("share", (int) MathF.Round(_transfer * coverage * 100f))));
        return msg;
    }
}
