using Content.Server.Atmos.Components;
using Content.Shared._WH40K.BloodTrail;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.BloodTrail;

/// <summary>
/// Tracks liquid stains on mobs touching puddles with strict cooldown throttling and lazy expiry.
/// Produces zero server decals and zero per-step MoveEvent load; clients render all footprints locally.
/// </summary>
public sealed partial class Wh40kBloodStainSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float WaterShare = 0.8f;
    private const float FullPuddleVolume = 5f;
    private const float MinPuddleVolume = 1.5f;
    private const string SuitSlot = "outerClothing";

    private static readonly TimeSpan StainCooldown = TimeSpan.FromSeconds(0.6);
    private static readonly TimeSpan StainLifetime = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);
    private static readonly ProtoId<ReagentPrototype> Water = "Water";

    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<PressureProtectionComponent> _pressureQuery;

    private TimeSpan _nextSweep;
    private GameTick _cleanTick;
    private readonly HashSet<(EntityUid Grid, Vector2i Tile, float Radius)> _cleanedThisTick = new();

    public override void Initialize()
    {
        base.Initialize();

        _mobQuery = GetEntityQuery<MobStateComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _pressureQuery = GetEntityQuery<PressureProtectionComponent>();

        SubscribeLocalEvent<PuddleComponent, StartCollideEvent>(OnPuddleStartCollide);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;

        var query = EntityQueryEnumerator<Wh40kBloodStainComponent>();
        while (query.MoveNext(out var uid, out var stain))
        {
            if (now >= stain.ExpiresAt)
                RemCompDeferred<Wh40kBloodStainComponent>(uid);
        }
    }

    private void OnPuddleStartCollide(Entity<PuddleComponent> ent, ref StartCollideEvent args)
    {
        var other = args.OtherEntity;
        if (!args.OtherFixture.Hard || !_mobQuery.HasComp(other))
            return;

        var now = _timing.CurTime;
        if (TryComp<Wh40kBloodStainComponent>(other, out var existing) && now < existing.NextStainAllowed)
            return;

        if (!IsGrounded(other))
            return;

        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out _, out var solution)
            || solution.Volume.Float() < MinPuddleVolume)
            return;

        if (solution.GetTotalPrototypeQuantity(Water) > solution.Volume * WaterShare)
            return;

        if (!_appearance.TryGetData<Color>(ent, PuddleVisuals.SolutionColor, out var color) || color.A <= 0.01f)
            return;

        var strength = Math.Clamp(solution.Volume.Float() / FullPuddleVolume, 0.35f, 1f);
        Stain(other, color, strength);
    }

    /// <summary>Stains a mob so clients draw a short fading trail of footprints or drag marks behind it.</summary>
    public void Stain(EntityUid uid, Color color, float strength = 1f, byte maxPrints = 6)
    {
        if (color.A <= 0.01f || strength <= 0.01f)
            return;

        var now = _timing.CurTime;
        var comp = EnsureComp<Wh40kBloodStainComponent>(uid);
        if (now < comp.NextStainAllowed)
            return;

        var clampedStrength = Math.Clamp(strength, 0.1f, 1f);
        comp.StainColor = comp.StainStrength > 0.05f
            ? Color.InterpolateBetween(comp.StainColor, color, 0.55f).WithAlpha(MathF.Max(comp.StainColor.A, color.A))
            : color;
        comp.StainStrength = MathF.Max(comp.StainStrength * 0.7f, clampedStrength);
        comp.MaxPrints = maxPrints;
        comp.HeavySuit = _inventory.TryGetSlotEntity(uid, SuitSlot, out var suit) && _pressureQuery.HasComp(suit);
        comp.StainEpoch++;
        comp.NextStainAllowed = now + StainCooldown;
        comp.ExpiresAt = now + StainLifetime;
        Dirty(uid, comp);
    }

    /// <summary>Broadcasts a lightweight PVS wipe so clients clear blood footprints around a cleaned tile.</summary>
    public void CleanTile(EntityUid gridUid, Vector2i tile, float radius = 0.85f)
    {
        if (!Exists(gridUid))
            return;

        // A mop can clean the same tile both through a reagent reaction and by absorbing the puddle.
        var tick = _timing.CurTick;
        if (_cleanTick != tick)
        {
            _cleanTick = tick;
            _cleanedThisTick.Clear();
        }
        if (!_cleanedThisTick.Add((gridUid, tile, radius)))
            return;

        var coords = new EntityCoordinates(gridUid, tile + new System.Numerics.Vector2(0.5f, 0.5f));
        RaiseNetworkEvent(new Wh40kBloodTrailCleanEvent(GetNetEntity(gridUid), tile, radius), Filter.Pvs(coords, entityMan: EntityManager));
    }

    private bool IsGrounded(EntityUid uid)
    {
        if (_gravity.IsWeightless(uid))
            return false;

        return !_physicsQuery.TryComp(uid, out var body) || body.BodyStatus == BodyStatus.OnGround;
    }
}
