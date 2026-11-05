using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ShipShields;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BastionShieldComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public float Load;
}

[Serializable, NetSerializable]
public sealed class BastionShieldImpactEvent(NetEntity shield, Vector2 position, TimeSpan time) : EntityEventArgs
{
    public readonly NetEntity Shield = shield;
    public readonly Vector2 Position = position;
    public readonly TimeSpan Time = time;
}
