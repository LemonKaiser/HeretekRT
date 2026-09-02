using System.Collections.Generic;

namespace Content.Server.Database;

/// <summary>
/// A bounded page of persistent administrative mute history.
/// </summary>
public sealed record WH40KMuteHistoryPage(
    List<WH40KMuteDef> Entries,
    bool HasNextPage);

/// <summary>
/// Summary of an atomic mute replacement operation.
/// </summary>
public sealed record WH40KMuteReplacementResult(
    int SupersededCount,
    int CreatedCount);
