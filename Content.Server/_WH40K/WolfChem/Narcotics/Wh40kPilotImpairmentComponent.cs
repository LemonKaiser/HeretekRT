using System.Numerics;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>What a pilot's high has done to their stick so far: the lagging input, the sway and the next twitch.</summary>
[RegisterComponent]
public sealed partial class Wh40kPilotImpairmentComponent : Component
{
    public Vector2 Strafe;
    public float Rotation;
    public float Brakes;

    public float Time;
    public float Seed;

    public float NextTwitch;
    public float TwitchLeft;
    public float TwitchTurn;
}
