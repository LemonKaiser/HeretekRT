using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Base component for limb/hand implants that fold an item out into the matching hand and retract it back.
/// Supports living on an <see cref="Content.Shared.Body.Organ.OrganComponent"/> inside a limb or directly on a body part.
/// </summary>
public abstract partial class Wh40kDeployableImplantComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> Items = new();

    /// <summary>Fallback action if side-specific actions are not configured.</summary>
    [DataField]
    public EntProtoId? Action;

    /// <summary>Action granted when installed into a left-side limb or hand.</summary>
    [DataField]
    public EntProtoId? LeftAction;

    /// <summary>Action granted when installed into a right-side limb or hand.</summary>
    [DataField]
    public EntProtoId? RightAction;

    [DataField]
    public SoundSpecifier? DeploySound;

    [DataField]
    public SoundSpecifier? RetractSound;

    [ViewVariables]
    public EntityUid? ActionEntity;

    [ViewVariables]
    public EntityUid? Deployed;

    /// <summary>
    /// Whether the deployed item's battery preserves charge while stowed and accrues offline recharge.
    /// </summary>
    [DataField]
    public bool KeepCharge;

    [ViewVariables]
    public (float Charge, TimeSpan At)? StoredCharge;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan EmpDisabledUntil = TimeSpan.Zero;
}

/// <summary>Retractable fingertip piezo-lighter.</summary>
[RegisterComponent]
public sealed partial class Wh40kFingerLighterComponent : Wh40kDeployableImplantComponent;

/// <summary>Retractable forearm laser emitter.</summary>
[RegisterComponent]
public sealed partial class Wh40kArmLaserComponent : Wh40kDeployableImplantComponent;

public sealed partial class Wh40kToggleDeployableImplantEvent : InstantActionEvent;

/// <summary>Raised on the implant entity before deploying an item into hand; cancelling prevents deployment.</summary>
[ByRefEvent]
public record struct Wh40kDeployAttemptEvent(EntityUid Body, bool Cancelled = false);

/// <summary>Raised on a body when a non-gun weapon (like the shoulder cannon) fires and should break optical camo.</summary>
[ByRefEvent]
public record struct Wh40kOpticalCamoRevealEvent;
