using Content.Server.Shuttles;
using Robust.Shared.Map;

namespace Content.Server._Mono.Shuttles.Components;

/// <summary>
/// Stores the per-shuttle opt-in setting for automatic docking.
/// </summary>
[RegisterComponent]
public sealed partial class AutoDockComponent : Component
{
    [DataField]
    public bool Enabled;
}

/// <summary>
/// Tracks the two stages of an automatic docking manoeuvre started by a shuttle console.
/// </summary>
[RegisterComponent]
public sealed partial class ShuttleConsoleAutoDockingComponent : Component
{
    public EntityUid TargetGrid;
    public EntityUid ShuttleDock;
    public EntityUid TargetDock;
    public EntityCoordinates ApproachCoordinates;
    public DockingConfig? Configuration;
    public AutoDockPhase Phase;
    public float TerminalElapsed;
    public byte StableTerminalTicks;
    public bool CollisionDetected;
}

public enum AutoDockPhase : byte
{
    Approach,
    TerminalApproach,
}
