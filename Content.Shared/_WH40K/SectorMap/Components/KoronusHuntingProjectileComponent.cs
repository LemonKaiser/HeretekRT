using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.SectorMap.Components;

/// <summary>Surface defence ammunition that passes through players and non-hostile mobs.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KoronusHuntingProjectileComponent : Component;
