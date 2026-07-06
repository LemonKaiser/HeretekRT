namespace Content.Shared._WH40K.Combat;

/// <summary>
/// Allows projectiles and hitscan traces to selectively pass depending on barricade facing.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KDirectionalBarricadeComponent : Component
{
    [DataField("passSideMaxDistance")]
    public float PassSideMaxDistance = 2f;

    [DataField("blockedSidePassChance")]
    public float BlockedSidePassChance = 0.05f;

    [DataField("blockedSidePointBlankPassDistance")]
    public float BlockedSidePointBlankPassDistance = 1f;

    [DataField("flipPassSide")]
    public bool FlipPassSide;
}
