using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.WolfChem.Narcotics;

/// <summary>
/// Marks a non-organic brain organ or synthetic body. A body whose brain has this ignores chemical doses and runs neuroware instead.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class Wh40kNeurowareHostComponent : Component;
