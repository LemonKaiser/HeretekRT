using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Synthetics;

/// <summary>
/// Torso organ for Cybernetic (Mechanicus) and IPC chassis that absorbs incoming radiation up to
/// <see cref="Capacity"/>. Overflow degrades <see cref="Integrity"/> and leaks half into body radiation damage.
/// Removed via the chassis bay, it vents charge passively and can be welded back to full integrity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kRadCollectorComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Charge;

    [DataField]
    public float Capacity = 300f;

    [DataField, AutoNetworkedField]
    public float Integrity = 30f;

    [DataField]
    public float MaxIntegrity = 30f;

    [DataField]
    public float VentPerSecond = 5f;

    [DataField]
    public float DrawPerSecond = 2f;

    [DataField]
    public float StrainPerRad = 0.03f;

    [DataField]
    public float LeakDamagePerRad = 0.01f;

    [DataField]
    public float WeldRepair = 10f;

    [DataField]
    public float WeldTime = 3f;
}

[Serializable, NetSerializable]
public sealed partial class Wh40kRadCollectorWeldDoAfterEvent : SimpleDoAfterEvent;
