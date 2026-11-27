using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Subdermal cameleoline optical camouflage array: grants temporary stealth that breaks on combat, external damage, or EMP.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kOpticalCamoComponent : Component
{
    [DataField]
    public EntProtoId Action = "WH40KActionOpticalCamo";

    [ViewVariables]
    public EntityUid? ActionEntity;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    [DataField]
    public TimeSpan EmpCooldown = TimeSpan.FromSeconds(120);

    [DataField(required: true)]
    public ComponentRegistry Components = new();

    [DataField]
    public SoundSpecifier? OnSound = new SoundPathSpecifier("/Audio/Effects/stealthoff.ogg");

    [DataField]
    public SoundSpecifier? OffSound = new SoundPathSpecifier("/Audio/Effects/stealthoff.ogg");

    [ViewVariables]
    public TimeSpan? EndsAt;
}

public sealed partial class Wh40kToggleOpticalCamoEvent : InstantActionEvent;
