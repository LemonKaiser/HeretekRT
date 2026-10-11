using Robust.Shared.Map;

namespace Content.Server._WH40K.Npcs;

/// <summary>Runtime facing captured before native seating changes the NPC's parent.</summary>
[RegisterComponent]
public sealed partial class Wh40kNpcFacingComponent : Component
{
    [ViewVariables] public EntityCoordinates Home;
    [ViewVariables] public Angle Facing;
}
