using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Lathe;

/// <summary>
/// What a queued batch still needs before its next item can start, counting linked silos.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kLatheMissingSupplies
{
    public bool DesignAvailable = true;
    public Dictionary<ProtoId<MaterialPrototype>, int> Materials = new();
    public Dictionary<EntProtoId, int> Entities = new();
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Reagents = new();

    public bool Ready => DesignAvailable && Materials.Count == 0 && Entities.Count == 0 && Reagents.Count == 0;
}

/// <summary>
/// Updates the requested total of a queued lathe batch.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kLatheRecipeAmountMessage(int index, int amount) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
    public readonly int Amount = amount;
}
