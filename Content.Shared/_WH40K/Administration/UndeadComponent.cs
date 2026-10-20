using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Administration;

/// <summary>
/// Allows ordinary damage and effects, but rejuvenates before incapacitation or destruction.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class UndeadComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Deaths;

    [DataField, AutoNetworkedField]
    public FixedPoint2 TotalDamage;

    [DataField, AutoNetworkedField]
    public FixedPoint2 LastDamage;

    // Body parts are damaged before their owning body. Delay their recovery report until
    // the entire hit has landed so the report includes the body's full, resisted damage.
    public int DamageDepth;
    public bool RecoveryPending;
    public bool Recovering;
}
