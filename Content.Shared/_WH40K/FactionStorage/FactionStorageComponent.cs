using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.FactionStorage;

[RegisterComponent]
public sealed partial class FactionStorageComponent : Component
{
    public const int Capacity = 100;

    [DataField(required: true)]
    public ProtoId<CompanyPrototype> Faction;
}
