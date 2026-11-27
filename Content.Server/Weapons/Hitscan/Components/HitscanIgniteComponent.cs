namespace Content.Server.Weapons.Hitscan.Components;

/// <summary>
/// Grants hitscan beams a probability to ignite flammable targets and apply fire stacks.
/// </summary>
[RegisterComponent]
public sealed partial class HitscanIgniteComponent : Component
{
    [DataField]
    public float IgniteChance = 0.25f;

    [DataField]
    public float FireStacks = 1f;
}
