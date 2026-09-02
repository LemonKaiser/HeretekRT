using Robust.Shared.Player;

namespace Content.Shared.Chat;

/// <summary>
///     Raised before an entity sends a LOOC or dead-chat message on behalf of a player.
/// </summary>
[ByRefEvent]
public record struct InGameOocMessageAttemptEvent(ICommonSession Session, InGameOOCChatType Type, bool Cancelled = false);
