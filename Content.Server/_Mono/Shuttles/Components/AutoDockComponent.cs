using Content.Server.Shuttles;
using Robust.Shared.Map;

namespace Content.Server._Mono.Shuttles.Components;

/// <summary>
/// Stores the per-shuttle setting for automatic docking (enabled by default).
/// </summary>
[RegisterComponent]
public sealed partial class AutoDockComponent : Component
{
    [DataField]
    public bool Enabled = true;
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
    public byte Retries;
    public float NextClearanceCheck;
    public float BestTerminalError;
    public float SecondsWithoutProgress;
    public Angle? LateralCorrectionAngle;
    public bool RetreatingForLateralCorrection;
    public bool LateralTurnStarted;
}

public enum AutoDockPhase : byte
{
    Approach,
    TerminalApproach,
}
