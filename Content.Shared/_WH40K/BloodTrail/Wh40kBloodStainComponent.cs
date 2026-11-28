using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.BloodTrail;

/// <summary>
/// Lightweight stain state replicated only while a mob has liquid on its boots or body.
/// Actual footprints and drag marks are rendered purely client-side in a bounded overlay buffer
/// with zero server decal mutations or per-step network traffic.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class Wh40kBloodStainComponent : Component
{
    /// <summary>Tint color of the stained liquid (blood, ichor, oil, slime, etc.).</summary>
    [DataField, AutoNetworkedField]
    public Color StainColor = Color.Transparent;

    /// <summary>Initial opacity multiplier in [0..1], scaled down for tiny puddles.</summary>
    [DataField, AutoNetworkedField]
    public float StainStrength;

    /// <summary>How many prints a fresh stain produces before wearing off.</summary>
    [DataField, AutoNetworkedField]
    public byte MaxPrints = 6;

    /// <summary>True when wearing heavy/pressurized outer armor so prints use the heavy suit boot shape.</summary>
    [DataField, AutoNetworkedField]
    public bool HeavySuit;

    /// <summary>Monotonic counter bumped on each fresh stain so clients refill their local step charges.</summary>
    [DataField, AutoNetworkedField]
    public int StainEpoch;

    /// <summary>Server-only cooldown preventing puddle wiggle spam.</summary>
    [ViewVariables]
    public TimeSpan NextStainAllowed;

    /// <summary>Server-only expiration time for lazy cleanup without per-step MoveEvent tracking.</summary>
    [ViewVariables]
    public TimeSpan ExpiresAt;
}

/// <summary>
/// Tiny PVS event notifying clients to clear client-rendered blood prints around a mopped or cleaned tile.
/// </summary>
[Serializable, NetSerializable]
public sealed class Wh40kBloodTrailCleanEvent : EntityEventArgs
{
    public readonly NetEntity Grid;
    public readonly Vector2i Tile;
    public readonly float Radius;

    public Wh40kBloodTrailCleanEvent(NetEntity grid, Vector2i tile, float radius = 0.85f)
    {
        Grid = grid;
        Tile = tile;
        Radius = radius;
    }
}
