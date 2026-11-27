using Content.Shared._WH40K.Biotype;

namespace Content.Shared.Traits;

public sealed partial class TraitPrototype
{
    [DataField]
    public HashSet<Wh40kBiotype> BiotypeBlacklist = new();
}
