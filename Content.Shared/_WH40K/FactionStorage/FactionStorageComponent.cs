using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.FactionStorage;

[RegisterComponent]
public sealed partial class FactionStorageComponent : Component
{
    public const int Capacity = 100;
    public const string RequestDelayId = "wh40k-faction-storage-request";
    public static readonly TimeSpan RequestCooldown = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan RefreshCooldown = TimeSpan.FromSeconds(1);

    [DataField(required: true)]
    public ProtoId<CompanyPrototype> Faction;
}
