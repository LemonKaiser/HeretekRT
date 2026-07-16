namespace Content.Shared._Mono;

/// <summary>
/// Applies and maintains NoHack and NoDeconstruct on selected authored entities of a grid.
/// Procedural terrain is not included because the component belongs to the facility grid itself.
/// </summary>
[RegisterComponent]
public sealed partial class GridRaiderComponent : Component
{
    /// <summary>
    /// Runtime entities currently tracked by this grid, including entities whose protection predated this component.
    /// The field remains readable for compatibility with existing maps, but must not be written back to map YAML:
    /// EntityUid values are runtime bookkeeping and are rebuilt by GridRaiderSystem.
    /// </summary>
    [DataField(readOnly: true)]
    public HashSet<EntityUid> ProtectedEntities = new();

    /// <summary>
    /// Runtime NoHack components created by GridRaiderSystem.
    /// </summary>
    [DataField(readOnly: true)]
    public HashSet<EntityUid> AddedNoHackEntities = new();

    /// <summary>
    /// Runtime NoDeconstruct components created by GridRaiderSystem.
    /// </summary>
    [DataField(readOnly: true)]
    public HashSet<EntityUid> AddedNoDeconstructEntities = new();

    /// <summary>
    /// Whether to protect entities with Door components.
    /// </summary>
    [DataField]
    public bool ProtectDoors = true;

    /// <summary>
    /// Whether to protect entities with VendingMachine components.
    /// </summary>
    [DataField]
    public bool ProtectVendingMachines = true;

    /// <summary>
    /// Protects every constructable machine or structure on an authored facility.
    /// </summary>
    [DataField]
    public bool ProtectConstructables = true;
}
