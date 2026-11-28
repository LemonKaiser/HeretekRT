using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Lathe;

[Serializable, NetSerializable]
public sealed class EjectWh40kFabricationSiloPartMessage(NetEntity part) : BoundUserInterfaceMessage
{
    public readonly NetEntity Part = part;
}

[Serializable, NetSerializable]
public sealed class WithdrawWh40kFabricationSiloReagentMessage(ProtoId<ReagentPrototype> reagent, FixedPoint2? amount)
    : BoundUserInterfaceMessage
{
    public readonly ProtoId<ReagentPrototype> Reagent = reagent;
    public readonly FixedPoint2? Amount = amount;
}

[Serializable, NetSerializable]
public sealed class DiscardWh40kFabricationSiloReagentMessage(ProtoId<ReagentPrototype> reagent) : BoundUserInterfaceMessage
{
    public readonly ProtoId<ReagentPrototype> Reagent = reagent;
}
