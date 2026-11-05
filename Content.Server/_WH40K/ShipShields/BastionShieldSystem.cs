using System.Numerics;
using Content.Server._WH40K.SectorMap.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.ShipShields;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.ShipShields;

public sealed class BastionShieldSystem : EntitySystem
{
    private static readonly TimeSpan ImpactInterval = TimeSpan.FromMilliseconds(50);

    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextImpact = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<BastionShieldComponent, ComponentShutdown>(OnShutdown);
    }

    public void UpdateVisuals(EntityUid shield, EntityUid grid, EntityUid? source)
    {
        // ShuttleComponent is added to every grid. Station membership alone also includes
        // station-owned shuttles, so require a stationary facility and exclude vessel ownership.
        if (!TryComp<PhysicsComponent>(grid, out var body) || body.BodyType != BodyType.Static ||
            HasComp<VesselComponent>(grid) || HasComp<ShipOwnershipComponent>(grid) ||
            !HasComp<StationMemberComponent>(grid) && !HasComp<KoronusProtectedFacilityComponent>(grid))
        {
            RemComp<BastionShieldComponent>(shield);
            return;
        }

        var visual = EnsureComp<BastionShieldComponent>(shield);
        var load = 0f;
        if (source is { } emitterUid && TryComp<ShipShieldEmitterComponent>(emitterUid, out var emitter))
            load = MathF.Round(Math.Clamp(emitter.Damage / Math.Max(emitter.DamageLimit, 1f), 0f, 1f) * 20f) / 20f;

        if (visual.Load == load)
            return;

        visual.Load = load;
        Dirty(shield, visual);
    }

    public void ShowImpact(EntityUid uid, ShipShieldEmitterComponent emitter, MapCoordinates position)
    {
        if (emitter.Shield is not { } shield || emitter.Shielded is not { } grid)
            return;

        UpdateVisuals(shield, grid, uid);
        if (!HasComp<BastionShieldComponent>(shield))
            return;

        var now = _timing.CurTime;
        if (_nextImpact.TryGetValue(shield, out var next) && now < next)
            return;

        _nextImpact[shield] = now + ImpactInterval;
        var local = Vector2.Transform(position.Position, _transform.GetInvWorldMatrix(shield));
        RaiseNetworkEvent(new BastionShieldImpactEvent(GetNetEntity(shield), local, now),
            Filter.Pvs(_transform.ToCoordinates(position), entityMan: EntityManager));
    }

    private void OnShutdown(EntityUid uid, BastionShieldComponent component, ComponentShutdown args)
    {
        _nextImpact.Remove(uid);
    }
}
