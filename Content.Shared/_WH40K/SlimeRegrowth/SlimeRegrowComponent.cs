using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.SlimeRegrowth;

/// <summary>
/// Lets a slime person regrow one missing non-vital limb by spending food and water.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeRegrowComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Action;

    [DataField]
    public EntProtoId ActionPrototype = "ActionHeretekSlimeRegrowLimb";

    [DataField, AutoNetworkedField]
    public float HungerCost = 60f;

    [DataField, AutoNetworkedField]
    public float ThirstCost = 100f;

    [DataField]
    public LocId SuccessPopup = "slime-regrow-limb-success";

    [DataField]
    public LocId NoLimbPopup = "slime-regrow-limb-none";

    [DataField]
    public LocId TooHungryPopup = "slime-regrow-limb-too-hungry";

    [DataField]
    public LocId TooThirstyPopup = "slime-regrow-limb-too-thirsty";

    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Voice/Slime/slime_squish.ogg");
}
