using Robust.Shared.Audio;

namespace Content.Shared._WH40K.Bosses;

[RegisterComponent]
public sealed partial class Wh40kBossComponent : Component
{
    [DataField] public string Title = "wh40k-boss-default-name";
    [DataField] public bool Enabled = true;
    [DataField] public float AwarenessRadius = 1000f;
    [DataField] public bool SameGridOnly;
    [DataField] public int Priority;
    [DataField] public SoundSpecifier? Theme;
    [DataField] public float ThemeFadeIn = 0.5f;
    [DataField] public float ThemeFadeOut = 1f;
}
