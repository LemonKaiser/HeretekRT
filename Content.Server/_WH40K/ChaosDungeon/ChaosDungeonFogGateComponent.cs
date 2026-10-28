using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.ChaosDungeon;

[RegisterComponent]
public sealed partial class ChaosDungeonFogGateComponent : Component
{
    [DataField]
    public Vector2 ExitOffset = new(0, -1.5f);

    public EntityUid Instance;
}

[RegisterComponent]
public sealed partial class ChaosDungeonBossSpawnComponent : Component
{
    [DataField]
    public EntProtoId Prototype = "MobWH40KChaosMarine";

    [DataField]
    public Vector2 ArenaCenterOffset = new(0, -3);
}

[RegisterComponent]
public sealed partial class ChaosDungeonArenaMemberComponent : Component
{
    public EntityUid Gate;
    public bool IsBoss;
    public bool Returning;
}
