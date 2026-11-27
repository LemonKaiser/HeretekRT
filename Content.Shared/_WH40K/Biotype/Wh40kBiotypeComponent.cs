using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Biotype;

/// <summary>
/// Tracks the effective biotype applied to a spawned humanoid entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Wh40kBiotypeComponent : Component
{
    [DataField, AutoNetworkedField]
    public Wh40kBiotype Biotype = Wh40kBiotype.Organic;
}

/// <summary>
/// Raised locally on a mob after <see cref="Wh40kBiotype"/> conversion finishes.
/// </summary>
[ByRefEvent]
public readonly record struct Wh40kBiotypeAppliedEvent(Wh40kBiotype Biotype);
