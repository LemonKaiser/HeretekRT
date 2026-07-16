namespace Content.Shared._Mono;

/// <summary>
/// Component that applies GodMode to all non-organic entities on a grid.
/// </summary>
[RegisterComponent]
public sealed partial class GridGodModeComponent : Component
{
    /// <summary>
    /// Runtime list of entities that have been given GodMode by this component.
    /// EntityUid values are rebuilt by GridGodModeSystem and must not be written to map YAML.
    /// </summary>
    [DataField(readOnly: true)]
    public HashSet<EntityUid> ProtectedEntities = new();
}
