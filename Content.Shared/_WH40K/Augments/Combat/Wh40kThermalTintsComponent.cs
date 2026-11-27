using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Multi-spectral ocular suite cycling through thermal and night-vision phosphor bands with one action.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class Wh40kThermalTintsComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public List<Wh40kThermalTint> Tints = new();

    /// <summary>Current active band index, or -1 while vision augmentation is off.</summary>
    [DataField, AutoNetworkedField]
    public int Index = -1;

    [DataField]
    public EntProtoId Action = "WH40KActionCycleThermalTint";

    [ViewVariables]
    public EntityUid? ActionEntity;

    [DataField]
    public SoundSpecifier? SwapSound = new SoundPathSpecifier("/Audio/_White/Items/Goggles/activate.ogg");

    [DataField]
    public SoundSpecifier? ChirpSound = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg",
        AudioParams.Default.WithPitchScale(1.35f).WithVolume(-4f));

    [DataField]
    public SoundSpecifier? OffSound = new SoundPathSpecifier("/Audio/_White/Items/Goggles/deactivate.ogg");
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class Wh40kThermalTint
{
    [DataField]
    public bool NightVision;

    [DataField(required: true)]
    public Color Color;

    [DataField]
    public Vector3 Tint;
}

public sealed partial class Wh40kCycleThermalTintEvent : InstantActionEvent;

[ByRefEvent]
public readonly record struct Wh40kThermalTintSwappedEvent(Color Color);
