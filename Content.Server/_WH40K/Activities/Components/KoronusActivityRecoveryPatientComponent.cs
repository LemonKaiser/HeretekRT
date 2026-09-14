namespace Content.Server._WH40K.Activities.Components;

/// <summary>
/// Server-only marker for the one patient created by an authored rescue location. It is not a
/// medical bounty, has no ghost takeover and grants no cash; the id is used only to preserve the
/// patient while the originating location is cleaned up.
/// </summary>
[RegisterComponent, Access(typeof(KoronusActivitySetPieceExecutorSystem), typeof(KoronusFootfallTrophyReceiverSystem))]
public sealed partial class KoronusActivityRecoveryPatientComponent : Component
{
    [ViewVariables]
    public long InstanceId;
}
