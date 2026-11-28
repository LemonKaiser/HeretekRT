using Content.Shared._WH40K.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Lathe;

public sealed partial class LatheUpdateState
{
    public HashSet<ProtoId<LatheRecipePrototype>> ReadyRecipes = new();
    public Dictionary<int, Wh40kLatheMissingSupplies> QueueSupplies = new();
    public int? PrintingBatch;
}
