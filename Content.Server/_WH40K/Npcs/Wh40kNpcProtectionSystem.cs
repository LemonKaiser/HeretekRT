using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Shared._WH40K.Npcs;
using Content.Server.Carrying;
using Content.Server.Access.Systems;
using Content.Server.Humanoid.Systems;
using Content.Shared.Clothing;
using Content.Shared.Inventory;
using Content.Shared.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Rejuvenate;
using Robust.Shared.Containers;

namespace Content.Server._WH40K.Npcs;

/// <summary>Reuse Godmode and Unremoveable for authored NPCs and their private equipment.</summary>
public sealed partial class Wh40kNpcProtectionSystem : EntitySystem
{
    [Dependency] private SharedGodmodeSystem _godmode = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedBodySystem _bodySystem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private Wh40kNpcAnchorSystem _anchors = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IdCardSystem _idCards = default!;
    private float _timer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kNpcProtectionComponent, MapInitEvent>(OnInit,
            after: [typeof(LoadoutSystem), typeof(RandomHumanoidAppearanceSystem)]);
        SubscribeLocalEvent<Wh40kNpcProtectionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kNpcProtectionComponent, RejuvenateEvent>(OnRejuvenate, after: [typeof(DamageableSystem)]);
        SubscribeLocalEvent<Wh40kNpcProtectionComponent, ContainerGettingInsertedAttemptEvent>(OnInsert);
        SubscribeLocalEvent<Wh40kNpcProtectionComponent, EntInsertedIntoContainerMessage>(OnGearInserted);
        SubscribeLocalEvent<Wh40kNpcSpawnComponent, MapInitEvent>(OnSpawn,
            after: [typeof(LoadoutSystem), typeof(RandomHumanoidAppearanceSystem)]);
    }

    private void OnStartup(EntityUid uid, Wh40kNpcProtectionComponent comp, ComponentStartup ev)
        => ApplyProtection(uid, comp, initialize: true);

    private void ApplyProtection(EntityUid uid, Wh40kNpcProtectionComponent comp, bool initialize = false)
    {
        // Native starting-gear internals checks require a respirator; disable both as one protected-NPC policy.
        RemComp<InternalsComponent>(uid);
        RemComp<RespiratorComponent>(uid);
        RemComp<BarotraumaComponent>(uid);

        if (comp.PreserveDamage)
        {
            EnsureComp<GodmodeComponent>(uid);
        }
        else if (initialize || !HasComp<GodmodeComponent>(uid))
        {
            // Enabling godmode rejuvenates the entire body. Existing protection only needs
            // to be restored when the component is missing, not reapplied every second.
            _godmode.EnableGodmode(uid);
        }

        foreach (var (id, _) in _bodySystem.GetBodyChildren(uid))
            EnsureComp<GodmodeComponent>(id);
    }

    private void OnInit(EntityUid uid, Wh40kNpcProtectionComponent comp, MapInitEvent ev)
    {
        comp.Home ??= Transform(uid).Coordinates;
        RemCompDeferred<CarriableComponent>(uid);
        RemComp<InternalsComponent>(uid);
        RemComp<RespiratorComponent>(uid);
        RemComp<BarotraumaComponent>(uid);
        LockContents(uid);
        UpdateIdentity(uid);

        if (comp.PreserveDamage && TryComp<DamageableComponent>(uid, out var damageable))
        {
            comp.PreservedDamage ??= new DamageSpecifier(damageable.Damage);
            _damageable.DamageChanged(uid, damageable, damageable.Damage);
        }
    }

    private void OnRejuvenate(EntityUid uid, Wh40kNpcProtectionComponent comp, RejuvenateEvent ev)
    {
        if (comp.PreserveDamage && comp.PreservedDamage != null && TryComp<DamageableComponent>(uid, out var damageable))
            _damageable.SetDamage(uid, damageable, comp.PreservedDamage);
    }

    private void OnGearInserted(EntityUid uid, Wh40kNpcProtectionComponent comp, EntInsertedIntoContainerMessage ev)
    {
        LockContents(uid);
        UpdateIdentity(uid);
    }

    private void UpdateIdentity(EntityUid uid)
    {
        // Resolve the worn credential (including PDAs), never a card held for somebody else.
        if (_inventory.TryGetSlotEntity(uid, "id", out var worn) &&
            _idCards.TryGetIdCard(worn.Value, out var card))
            _idCards.TryChangeFullName(card, Name(uid), card);
    }

    public void LockContents(EntityUid owner)
    {
        if (!TryComp(owner, out ContainerManagerComponent? manager)) return;
        foreach (var container in manager.Containers.Values)
        foreach (var item in container.ContainedEntities)
        {
            EnsureComp<Wh40kNpcGearComponent>(item);
            EnsureComp<UnremoveableComponent>(item).DeleteOnDrop = false;
            LockContents(item);
        }
    }

    private void OnInsert(EntityUid uid, Wh40kNpcProtectionComponent comp, ContainerGettingInsertedAttemptEvent ev) => ev.Cancel();

    private void OnSpawn(EntityUid uid, Wh40kNpcSpawnComponent comp, MapInitEvent ev)
    {
        if (!TryComp(uid, out Wh40kNpcAnchorComponent? anchor) ||
            !_anchors.TryGetAnchor(anchor.Anchor, out var marker) || marker != uid || _anchors.HasRegisteredBody(anchor.Anchor)) return;
        var xform = Transform(uid);
        SpawnAttachedTo(comp.Prototype, xform.Coordinates, rotation: xform.LocalRotation);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 1f) return;
        _timer = 0;
        var query = EntityQueryEnumerator<Wh40kNpcProtectionComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            ApplyProtection(uid, comp);
            if (comp.PreserveDamage && comp.PreservedDamage != null &&
                TryComp<DamageableComponent>(uid, out var damageable) && damageable.Damage.Empty)
            {
                _damageable.SetDamage(uid, damageable, comp.PreservedDamage);
            }

            RemCompDeferred<CarriableComponent>(uid);
            LockContents(uid);
            var home = comp.Home;
            if (TryComp(uid, out Wh40kNpcIdentityComponent? identity))
            {
                if (_anchors.TryGetAnchor(identity.Anchor, out var marker)) home = Transform(marker).Coordinates;
            }
            if (home is not {} point || !point.IsValid(EntityManager)) continue;
            var destination = _transform.ToMapCoordinates(point);
            var current = _transform.GetMapCoordinates(uid, xform);
            if (current.MapId != destination.MapId || (current.Position - destination.Position).LengthSquared() > comp.HomeRadius * comp.HomeRadius)
            {
                // Detach native seat/relay state before home recovery; moving a chair must not move the NPC indefinitely.
                if (TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled)
                    _buckle.Unbuckle((uid, buckle), null);
                _transform.SetCoordinates(uid, point);
            }
        }
    }
}
