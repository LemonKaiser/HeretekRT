using Content.Server._Mono.Drone.Components;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WH40K.Drones.Components;
using Content.Server.NPC.HTN;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.Drones.Systems;

/// <summary>
/// Turns disabled hostile drones into identifiable wreckage and stops their remaining AI.
/// </summary>
public sealed partial class Wh40kDroneDefeatSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly ShipSteeringSystem _steering = default!;
    [Dependency] private readonly ShipTargetingSystem _targeting = default!;

    public const string WreckageNameLoc = "wh40k-drone-wreckage-name";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DroneAiCoreComponent, MapInitEvent>(OnCoreMapInit);
    }

    private void OnCoreMapInit(EntityUid uid, DroneAiCoreComponent component, MapInitEvent args)
    {
        if (Transform(uid).GridUid is { } grid &&
            TryComp<HostileDroneGridComponent>(grid, out var drone) && !drone.Initialized)
            CheckDroneStatus(grid, drone);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<HostileDroneGridComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var drone, out _))
        {
            if (drone.Defeated || Paused(uid))
                continue;

            drone.CheckAccumulator += frameTime;
            if (drone.CheckAccumulator < drone.CheckInterval)
                continue;

            drone.CheckAccumulator = 0f;
            CheckDroneStatus(uid, drone);
        }
    }

    private bool CheckDroneStatus(EntityUid gridUid, HostileDroneGridComponent drone)
    {
        if (TerminatingOrDeleted(gridUid) || !HasComp<MapGridComponent>(gridUid))
            return false;

        if (drone.Defeated)
            return true;

        var hasAi = false;
        var hasPoweredAi = false;
        var hasThrusters = false;
        var hasWeapons = false;
        var children = Transform(gridUid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TerminatingOrDeleted(child) || !Transform(child).Anchored)
                continue;

            // Boarders and other NPCs must not count as the ship's AI core.
            if (HasComp<DroneAiCoreComponent>(child) && TryComp<HTNComponent>(child, out var htn) && htn.Enabled)
            {
                hasAi = true;
                hasPoweredAi |= _power.IsPowered(child);
            }

            if (TryComp<ThrusterComponent>(child, out var thruster) &&
                thruster.Type == ThrusterType.Linear && thruster.Enabled)
                hasThrusters = true;

            if (HasComp<GunComponent>(child))
                hasWeapons = true;
        }

        // AI cores spawn from markers during MapInit, after the grid itself is initialized.
        // Record equipment when a core appears and allow power networks time to connect.
        drone.HadAi |= hasAi;
        drone.HadThrusters |= hasThrusters;
        drone.HadWeapons |= hasWeapons;
        if (!drone.Initialized)
        {
            drone.Initialized = hasAi;
            return false;
        }

        if ((!drone.HadAi || hasAi && hasPoweredAi) &&
            (!drone.HadThrusters || hasThrusters) &&
            (!drone.HadWeapons || hasWeapons) &&
            (hasThrusters || hasWeapons))
            return false;

        drone.Defeated = true;
        _shuttle.SetIFFColor(gridUid, Color.White);
        _shuttle.AddIFFFlag(gridUid, IFFFlags.AlwaysShowColor);
        _metaData.SetEntityName(gridUid, Loc.GetString(WreckageNameLoc));

        children = Transform(gridUid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TerminatingOrDeleted(child) || !HasComp<DroneAiCoreComponent>(child))
                continue;

            if (TryComp<HTNComponent>(child, out var htn))
                _htn.SetHTNEnabled((child, htn), false);

            _steering.Stop(child);
            _targeting.Stop(child);
        }

        return true;
    }
}
