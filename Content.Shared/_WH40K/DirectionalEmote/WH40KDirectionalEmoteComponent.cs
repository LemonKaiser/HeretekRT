using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.DirectionalEmote;

/// <summary>
/// Allows a player-controlled entity to exchange private, short-range roleplay emotes with another player.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KDirectionalEmoteComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool CanSendEmotes = true;

    [DataField, AutoNetworkedField]
    public bool CanReceiveEmotes = true;

    [DataField, AutoNetworkedField]
    public bool CanHideName;

    [AutoNetworkedField]
    public string LastEmote = string.Empty;

    public TimeSpan LastSendAt = TimeSpan.Zero;
    public TimeSpan Cooldown = TimeSpan.FromSeconds(2);
}

[Serializable, NetSerializable]
public sealed partial class WH40KDirectionalEmoteAttemptEvent : EntityEventArgs
{
    public NetEntity Target;
    public string Text;
    public bool HideName;

    public WH40KDirectionalEmoteAttemptEvent(NetEntity target, string text, bool hideName = false)
    {
        Target = target;
        Text = text;
        HideName = hideName;
    }
}
