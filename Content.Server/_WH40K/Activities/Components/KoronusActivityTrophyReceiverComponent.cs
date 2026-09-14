using Content.Shared._WH40K.Activities;

namespace Content.Server._WH40K.Activities.Components;

/// <summary>
/// A fixed, harmless Footfall hand-off counter. Its consumer category is an allow-list selector,
/// never a generic sell value or a prototype supplied by the item being handed over.
/// </summary>
[RegisterComponent, Access(typeof(KoronusFootfallTrophyReceiverSystem))]
public sealed partial class KoronusActivityTrophyReceiverComponent : Component
{
    [DataField(required: true)]
    public KoronusActivityTrophyConsumer Consumer;

    [DataField]
    public string PatientContainerId = "patient";
}
