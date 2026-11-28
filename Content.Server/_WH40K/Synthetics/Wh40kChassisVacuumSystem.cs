using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.Atmos;

namespace Content.Server._WH40K.Synthetics;

/// <summary>
/// Ensures a cybernetic/IPC chassis on a vacuum or near-vacuum tile loses heat toward cosmic background temperature
/// just like in open space, closing the exploit where unsuited synthetics walked across breached decks at room temp.
/// </summary>
public sealed class Wh40kChassisVacuumSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;

    private static readonly GasMixture Space = GasMixture.SpaceGas;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kChassisComponent, AtmosExposedUpdateEvent>(OnAtmosExposed);
    }

    private void OnAtmosExposed(Entity<Wh40kChassisComponent> ent, ref AtmosExposedUpdateEvent args)
    {
        if (args.GasMixture.Immutable || args.Transform.MapUid == null ||
            !TryComp<TemperatureComponent>(ent, out var temperature))
        {
            return;
        }

        var vacuum = 1f - args.GasMixture.Pressure / Atmospherics.HazardLowPressure;
        if (vacuum <= 0f)
            return;

        var spaceCapacity = _atmosphere.GetHeatCapacity(Space, false);
        _temperature.ExchangeHeatWithEnvironment(ent, temperature, Atmospherics.TCMB, spaceCapacity, vacuum);
    }
}
