namespace Content.Server._WH40K.Progression;

/// <summary>
/// Durable ECS half of a reward-outbox claim. It is kept on every issued entity until
/// the database acknowledges delivery and is serialized by persistent inventory.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kRewardDeliveryClaimComponent : Component
{
    [DataField]
    public Guid UserId;

    [DataField]
    public long DeliveryId;

    [DataField]
    public int ClaimAttempt;

    [DataField]
    public int EntityIndex;

    [DataField]
    public int ExpectedEntities;
}
