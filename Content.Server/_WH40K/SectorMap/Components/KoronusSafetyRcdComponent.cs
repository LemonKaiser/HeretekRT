namespace Content.Server._WH40K.SectorMap.Components;

/// <summary>
/// Server-side interaction hook added to every RCD so Koronus safety policy can run before the shared RCD handler.
/// A distinct component is required because an event can have only one subscription per component type.
/// </summary>
[RegisterComponent]
public sealed partial class KoronusSafetyRcdComponent : Component;
