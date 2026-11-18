namespace Content.Shared._WH40K.Visuals;

/// <summary>Overrides the cosmetic muzzle profile inherited from a weapon's class.</summary>
[RegisterComponent]
public sealed partial class WH40KMuzzleProfileComponent : Component
{
    [DataField(required: true)]
    public WH40KMuzzleProfile Profile;
}

public enum WH40KMuzzleProfile : byte
{
    Pistol,
    Automatic,
    MachineGun,
}
