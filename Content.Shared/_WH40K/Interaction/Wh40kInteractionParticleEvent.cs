using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Interaction;

/// <summary>
/// Data for interaction particles
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kInteractionParticleEvent(NetEntity performer, NetEntity? used, NetEntity target, bool isClientEvent, Wh40kInteractionParticleType type) : EntityEventArgs
{
    public NetEntity Performer = performer;

    public NetEntity? Used = used;

    public NetEntity Target = target;

    /// <summary>
    /// Workaround for event subscription not working w/ the session overload
    /// </summary>
    public bool IsClientEvent = isClientEvent;

    public Wh40kInteractionParticleType Type = type;
}

[Serializable, NetSerializable]
public enum Wh40kInteractionParticleType
{
    Use,
    Pull,
    InHand,
}
