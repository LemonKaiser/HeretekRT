using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Chat window opacity slider, controlling the alpha of the chat window background.
    ///     Goes from to 0 (completely transparent) to 1 (completely opaque)
    /// </summary>
    public static readonly CVarDef<float> ChatWindowOpacity =
        CVarDef.Create("accessibility.chat_window_transparency", 0.85f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Toggle for visual effects that may potentially cause motion sickness.
    ///     Where reasonable, effects affected by this CVar should use an alternate effect.
    ///     Please do not use this CVar as a bandaid for effects that could otherwise be made accessible without issue.
    /// </summary>
    public static readonly CVarDef<bool> ReducedMotion =
        CVarDef.Create("accessibility.reduced_motion", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> ChatEnableColorName =
        CVarDef.Create("accessibility.enable_color_name",
            true,
            CVar.CLIENTONLY | CVar.ARCHIVE,
            "Toggles displaying names with individual colors.");

    /// <summary>
    ///     Screen shake intensity slider, controlling the intensity of the CameraRecoilSystem.
    ///     Goes from 0 (no recoil at all) to 1 (regular amounts of recoil)
    /// </summary>
    public static readonly CVarDef<float> ScreenShakeIntensity =
        CVarDef.Create("accessibility.screen_shake_intensity", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Lobby background mode selection.
    ///     Supported values: <c>server</c>, <c>static</c>, <c>animated</c>.
    /// </summary>
    public static readonly CVarDef<string> LobbyBackgroundType =
        CVarDef.Create("accessibility.lobby_background_type", "server", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Preferred static lobby background prototype id.
    ///     Empty value means automatic fallback behavior.
    /// </summary>
    public static readonly CVarDef<string> LobbyStaticBackground =
        CVarDef.Create("accessibility.lobby_static_background", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Preferred animated lobby background prototype id.
    ///     Empty value means automatic fallback behavior.
    /// </summary>
    public static readonly CVarDef<string> LobbyAnimatedBackground =
        CVarDef.Create("accessibility.lobby_animated_background", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Lobby panel background opacity.
    ///     Goes from 0 (fully transparent) to 1 (fully opaque).
    /// </summary>
    public static readonly CVarDef<float> LobbyPanelOpacity =
        CVarDef.Create("accessibility.lobby_panel_opacity", 0.85f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     General UI window background opacity.
    ///     Goes from 0.75 (up to 25% transparent) to 1 (fully opaque).
    /// </summary>
    public static readonly CVarDef<float> UiWindowOpacity =
        CVarDef.Create("accessibility.ui_window_opacity", 0.95f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     A generic toggle for various visual effects that are color sensitive.
    ///     As of 2/16/24, only applies to progress bar colors.
    /// </summary>
    public static readonly CVarDef<bool> AccessibilityColorblindFriendly =
        CVarDef.Create("accessibility.colorblind_friendly", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Speech bubble text opacity slider, controlling the alpha of speech bubble's text.
    ///     Goes from to 0 (completely transparent) to 1 (completely opaque)
    /// </summary>
    public static readonly CVarDef<float> SpeechBubbleTextOpacity =
        CVarDef.Create("accessibility.speech_bubble_text_opacity", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Speech bubble speaker opacity slider, controlling the alpha of the speaker's name in a speech bubble.
    ///     Goes from to 0 (completely transparent) to 1 (completely opaque)
    /// </summary>
    public static readonly CVarDef<float> SpeechBubbleSpeakerOpacity =
        CVarDef.Create("accessibility.speech_bubble_speaker_opacity", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Speech bubble background opacity slider, controlling the alpha of the speech bubble's background.
    ///     Goes from to 0 (completely transparent) to 1 (completely opaque)
    /// </summary>
    public static readonly CVarDef<float> SpeechBubbleBackgroundOpacity =
        CVarDef.Create("accessibility.speech_bubble_background_opacity", 0.75f, CVar.CLIENTONLY | CVar.ARCHIVE);


}
