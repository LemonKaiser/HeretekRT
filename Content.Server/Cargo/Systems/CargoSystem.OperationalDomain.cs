using Content.Server.Cargo.Components;
using Content.Server._WH40K.OperationalDomain;
using Content.Shared._WH40K.OperationalDomain;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    [Dependency] private OperationalDomainSystem _operationalDomains = default!;

    internal bool TryResolveCargoDomain(EntityUid entity, out OperationalDomain domain)
    {
        return _operationalDomains.TryResolveOperationalDomain(entity, out domain);
    }

    /// <summary>
    /// Returns the order database owned by the domain containing the requested device.
    /// A vessel receives a database only through a cargo device; station data remains opt-in through
    /// its existing prototype component.
    /// </summary>
    internal bool TryResolveCargoOrderDatabase(
        EntityUid entity,
        out EntityUid owner,
        out StationCargoOrderDatabaseComponent database,
        bool createForVessel = true)
    {
        owner = EntityUid.Invalid;
        database = default!;
        if (!TryResolveCargoDomain(entity, out var domain))
            return false;

        owner = domain.Owner;
        if (TryComp<StationCargoOrderDatabaseComponent>(owner, out var existing) && existing != null)
        {
            database = existing;
            return true;
        }

        if (!createForVessel || domain.Kind != OperationalDomainKind.Vessel)
            return false;

        database = EnsureComp<StationCargoOrderDatabaseComponent>(owner);
        return true;
    }

    /// <summary>
    /// Returns the bounty database owned by the domain containing the requested device.
    /// Newly created vessel databases are populated immediately because MapInit has already passed.
    /// </summary>
    internal bool TryResolveCargoBountyDatabase(
        EntityUid entity,
        out EntityUid owner,
        out StationCargoBountyDatabaseComponent database,
        bool createForVessel = true)
    {
        owner = EntityUid.Invalid;
        database = default!;
        if (!TryResolveCargoDomain(entity, out var domain))
            return false;

        owner = domain.Owner;
        if (TryComp<StationCargoBountyDatabaseComponent>(owner, out var existing) && existing != null)
        {
            database = existing;
            return true;
        }

        if (!createForVessel || domain.Kind != OperationalDomainKind.Vessel)
            return false;

        database = EnsureComp<StationCargoBountyDatabaseComponent>(owner);
        FillBountyDatabase(owner, database);
        return true;
    }
}
