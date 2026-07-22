namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>
/// Server-side safety hook marker declared on every RCD so a Koronus safety policy can run before
/// the shared RCD interaction handler. It is shared because RCD prototypes are loaded by both sides.
/// </summary>
[RegisterComponent]
public sealed partial class KoronusSafetyRcdComponent : Component;
