using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.PersistentInventory;

/// <summary>
/// The server applies the policy to the complete physical item tree. The owner's role and profile are not policy inputs.
/// </summary>
[Prototype("persistentInventoryPolicy")]
public sealed partial class PersistentInventoryPolicyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public int Version { get; private set; } = 1;

    [DataField]
    public HashSet<string> DeniedPrototypes { get; private set; } = new();

    [DataField]
    public HashSet<string> DeniedPrototypePrefixes { get; private set; } = new();
}

/// <summary>
/// Unconditionally excludes an entity and its subtree from the persistent payload.
/// </summary>
[RegisterComponent]
public sealed partial class NoPersistentInventoryComponent : Component;

/// <summary>
/// Optional server-authoritative override for an ordinary item.
/// Absence of this component does not prohibit persistence.
/// </summary>
[RegisterComponent]
public sealed partial class PersistentInventoryItemComponent : Component
{
    [DataField]
    public string? Policy { get; private set; }

    [DataField]
    public bool AllowNestedContents { get; private set; } = true;
}
