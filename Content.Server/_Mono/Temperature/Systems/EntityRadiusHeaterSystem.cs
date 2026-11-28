using Content.Server._Mono.Temperature.Components;
using Content.Server.Atmos.Portable;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Portable.Components;
using Content.Shared.IgnitionSource;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Server._Mono.Temperature.Systems;

/// <summary>
/// Gives thermal energy to nearby entities.
/// </summary>
public sealed partial class EntityRadiusHeaterSystem : EntitySystem
{
    public const float MaxRadius = 5f;

    [Dependency] private TemperatureSystem _temp = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private float _updateCooldown = 1f;
    private TimeSpan _updateTimer = TimeSpan.Zero;

    public override void Update(float frameTime)
    {
        if (_updateTimer < TimeSpan.FromSeconds(_updateCooldown))
        {
            _updateTimer += TimeSpan.FromSeconds(frameTime);
            return;
        }

        var eqe = EntityQueryEnumerator<EntityRadiusHeaterComponent>();

        while (eqe.MoveNext(out var uid, out var comp))
        {
            if (!IsActive(uid, comp))
                continue;

            var nearby = _lookup.GetEntitiesInRange<TemperatureComponent>(Transform(uid).Coordinates, comp.Radius, LookupFlags.Dynamic | LookupFlags.Sundries);
            var xform = Transform(uid);
            foreach (var ent in nearby)
            {
                _temp.ChangeHeat(ent, CalculateThermalEnergy(ent, xform, comp));
            }
        }


        _updateTimer = TimeSpan.Zero;
    }

    /// <summary>
    /// Whether a radiant heater is currently active: lit/toggled on, powered, and not set to cool.
    /// </summary>
    public bool IsActive(EntityUid uid, EntityRadiusHeaterComponent comp)
    {
        if (comp.RequireActivation && TryComp<IgnitionSourceComponent>(uid, out var ignite)
                                   && !ignite.Ignited)
            return false;

        if (comp.RequireActivation && TryComp<ItemToggleComponent>(uid, out var toggle)
                                   && !toggle.Activated)
            return false;

        if (comp.RequireActivation && TryComp<FlammableComponent>(uid, out var flammable)
                                   && !flammable.OnFire)
            return false;

        if (TryComp<SpaceHeaterComponent>(uid, out var spaceHeater) && spaceHeater.Mode == SpaceHeaterMode.Cool)
            return false;

        return this.IsPowered(uid, EntityManager);
    }

    public float CalculateThermalEnergy(Entity<TemperatureComponent> ent,
        TransformComponent heaterXform,
        EntityRadiusHeaterComponent comp)
    {
        if (!Transform(ent).Coordinates.TryDistance(EntityManager, heaterXform.Coordinates, out var distance))
            return 0f;

        var c = 1 - (distance / comp.Radius);

        if (c < 0)
            return 0;

        var oT = _temp.GetHeatCapacity(ent) * ent.Comp.CurrentTemperature;
        var nT = Math.Clamp(c * comp.ThermalEnergy + oT, 0, _temp.GetHeatCapacity(ent) * comp.Limit);

        var d = nT - oT;

        if (d < 0)
            return 0;

        return d;
    }
}
