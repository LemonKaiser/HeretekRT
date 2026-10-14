using Robust.Shared.Map;

namespace Content.Server._WH40K.NPC;

[RegisterComponent]
/// <summary>A landing point known to the squad, used only while the grenade is in flight.</summary>
public sealed partial class Wh40kChaosGrenadeComponent : Component
{
    [ViewVariables]
    public EntityCoordinates? Destination;
}
