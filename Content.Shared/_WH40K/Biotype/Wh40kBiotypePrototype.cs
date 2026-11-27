using Content.Shared.Body.Prototypes;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Biotype;

/// <summary>
/// Defines the body prototype and component delta applied when converting a spawned humanoid
/// to a specific <see cref="Wh40kBiotype"/>.
/// </summary>
[Prototype("wh40kBiotype")]
public sealed partial class Wh40kBiotypePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Wh40kBiotype Biotype;

    [DataField]
    public ProtoId<BodyPrototype>? BodyPrototype;

    [DataField]
    public ProtoId<DamageContainerPrototype>? DamageContainer;

    [DataField]
    public ProtoId<DamageModifierSetPrototype>? DamageModifierSet;

    /// <summary>
    /// Optional power cell spawned into the entity's <c>cell_slot</c> if empty after applying components.
    /// </summary>
    [DataField]
    public EntProtoId? StartingPowerCell;

    [DataField]
    public HashSet<string> RemoveComponents = new();

    [DataField]
    public ComponentRegistry EnsureComponents = new();

    [DataField]
    public ComponentRegistry AddComponents = new();

    [DataField]
    public List<ProtoId<TagPrototype>> RemoveTags = new();

    [DataField]
    public List<ProtoId<TagPrototype>> AddTags = new();
}
