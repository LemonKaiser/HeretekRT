using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.ChaosDungeon;

public enum ChaosDungeonInstanceState : byte
{
    Arriving,
    Active,
    Completed,
    Failed,
    Escaped,
    Extracted,
}

[RegisterComponent]
public sealed partial class ChaosDungeonInstanceComponent : Component
{
    public ChaosDungeonInstanceState State = ChaosDungeonInstanceState.Arriving;

    public int Slot;
    public MapId MapId = MapId.Nullspace;
    public MapId SourceMapId = MapId.Nullspace;
    public Vector2 SourcePosition;
    public Angle SourceAngle;
    public EntityUid SourceShip = EntityUid.Invalid;
    public EntityUid DungeonGrid = EntityUid.Invalid;
    public EntityUid Boss = EntityUid.Invalid;
    public EntityUid BossSpawn = EntityUid.Invalid;
    public bool BossStarted;
    public TimeSpan? BossSpawnAt;
    public EntityUid BossSpawnGate = EntityUid.Invalid;
    public readonly List<EntityUid> FogGates = new();
    public readonly HashSet<EntityUid> ArenaMembers = new();

    public TimeSpan ExpiresAt;
    public TimeSpan ExtractionAt;
    public int LastWarningMinute = -1;
    public bool TimeoutWarningSent;

    public readonly Dictionary<NetUserId, EntityUid> Participants = new();
}
