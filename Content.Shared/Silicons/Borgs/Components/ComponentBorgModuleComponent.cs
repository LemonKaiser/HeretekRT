using Content.Shared._WH40K.BorgModules;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Silicons.Borgs.Components;

/// <summary>
/// A <see cref="BorgModuleComponent"/> that grants components to the borg chassis while installed.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedBorgSystem), typeof(BorgModuleExtrasSystem))]
public sealed partial class ComponentBorgModuleComponent : Component
{
    /// <summary>
    /// Components granted once this module is installed into a borg chassis.
    /// </summary>
    [DataField(required: true)]
    public ComponentRegistry Components = new();

    /// <summary>
    /// Component names actually added to the chassis on install (excluding components the chassis already had),
    /// so uninstalling never strips pre-existing chassis components.
    /// </summary>
    [ViewVariables]
    public HashSet<string> AppliedComponents = new();
}
