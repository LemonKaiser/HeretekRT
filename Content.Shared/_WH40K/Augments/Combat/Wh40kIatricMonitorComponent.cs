using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Augments.Combat;

/// <summary>
/// Internal medicae auspex monitor providing silent self health-analyzer diagnostics.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kIatricMonitorComponent : Component
{
    [DataField]
    public EntProtoId Action = "WH40KActionIatricScan";

    [ViewVariables]
    public EntityUid? ActionEntity;
}

/// <summary>
/// Marks the action entity carrying the iatric monitor's internal health analyzer UI.
/// </summary>
[RegisterComponent]
public sealed partial class Wh40kIatricScannerComponent : Component;

public sealed partial class Wh40kIatricScanEvent : InstantActionEvent;
