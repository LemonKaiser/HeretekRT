using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Lathe;

/// <summary>
/// Stores lathe recipe parts or reagents for linked lathes.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedWh40kFabricationSiloSystem))]
public sealed partial class Wh40kFabricationSiloComponent : Component
{
    public const string PartsContainerId = "fabrication_parts";
    public const string ContainerSlotId = "beakerSlot";

    [DataField]
    public Wh40kFabricationSiloKind Kind;

    [DataField, AutoNetworkedField]
    public float Range = 125f;

    [DataField]
    public int MaxParts = 250;

    [DataField]
    public string SolutionName = "tank";

    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> Clients = new();

    [ViewVariables]
    public Container? Parts;
}

[Serializable, NetSerializable]
public enum Wh40kFabricationSiloKind : byte
{
    Parts,
    Chemicals,
}
