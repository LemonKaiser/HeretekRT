using System.Collections.Immutable;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Shared.EntityList
{
    [Prototype]
    public sealed partial class EntityListPrototype : IPrototype
    {
        [ViewVariables]
        [IdDataField]
        public string ID { get; private set; } = default!;

        [DataField("entities")]
        public ImmutableList<EntProtoId> EntityIds { get; private set; } = ImmutableList<EntProtoId>.Empty;

        public IEnumerable<EntityPrototype> Entities(IPrototypeManager? prototypeManager = null)
        {
            prototypeManager ??= IoCManager.Resolve<IPrototypeManager>();

            foreach (var entityId in EntityIds)
            {
                yield return prototypeManager.Index<EntityPrototype>(entityId);
            }
        }
    }
}
