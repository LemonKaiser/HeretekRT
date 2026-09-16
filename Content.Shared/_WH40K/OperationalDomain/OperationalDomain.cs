namespace Content.Shared._WH40K.OperationalDomain;

/// <summary>
/// The kind of local owner selected for an operational subsystem.
/// </summary>
public enum OperationalDomainKind
{
    Station,
    Vessel,
}

/// <summary>
/// A local operational owner and the grid used by systems that need a physical location.
/// </summary>
public readonly record struct OperationalDomain(
    EntityUid Owner,
    EntityUid PrimaryGrid,
    OperationalDomainKind Kind);
