using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Bosses;

[Serializable, NetSerializable]
public sealed class Wh40kBossHudStateEvent : EntityEventArgs
{
    public Wh40kBossHudData? Boss;

    public Wh40kBossHudStateEvent(Wh40kBossHudData? boss)
    {
        Boss = boss;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kBossHudData
{
    // Boss is an identity only: its entity need not exist in the recipient's PVS.
    public NetEntity Boss;
    public NetEntity Map;
    public Vector2 Position;
    public string Title;
    public float Health;
    public float MaxHealth;
    public ResolvedSoundSpecifier? Theme;
    public AudioParams ThemeAudio;
    public float FadeIn;
    public float FadeOut;

    public Wh40kBossHudData(NetEntity boss, NetEntity map, Vector2 position, string title,
        float health, float maxHealth, ResolvedSoundSpecifier? theme, AudioParams themeAudio, float fadeIn, float fadeOut)
    {
        Boss = boss;
        Map = map;
        Position = position;
        Title = title;
        Health = health;
        MaxHealth = maxHealth;
        Theme = theme;
        ThemeAudio = themeAudio;
        FadeIn = fadeIn;
        FadeOut = fadeOut;
    }
}
