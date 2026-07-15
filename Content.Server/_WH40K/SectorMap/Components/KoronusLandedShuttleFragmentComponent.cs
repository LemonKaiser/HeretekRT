using Content.Server._WH40K.SectorMap.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Associates every grid fragment produced from a landed shuttle with its parking session.
/// </summary>
[RegisterComponent, Access(typeof(KoronusPlanetarySystem))]
public sealed partial class KoronusLandedShuttleFragmentComponent : Component
{
    public long SessionId;

    /// <summary>
    /// Physics state restored when this fragment leaves its landing pad.
    /// </summary>
    public BodyType OriginalBodyType;
    public BodyStatus OriginalBodyStatus;
    public bool OriginalFixedRotation;
    public bool PhysicsLocked;

    /// <summary>
    /// Only remove locks which were installed by the planetary parking system. Some special grids
    /// already own permanent locks which must survive a landing cycle.
    /// </summary>
    public bool AddedPreventPilot;
    public bool AddedPreventAnchorChanges;
}
