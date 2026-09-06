using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WH40K.CharacterCreation;
using Robust.Shared.Network;

namespace Content.Server._WH40K.CharacterCreation;

/// <summary>
/// Keeps the account-level onboarding state available for the lifetime of a connection.
/// The preferences manager owns load ordering; this manager owns the WH40K-specific cache.
/// </summary>
public sealed partial class Wh40kPlayerProgressManager
{
    [Dependency] private IServerDbManager _db = default!;

    private readonly Dictionary<NetUserId, Wh40kPlayerProgressSnapshot> _progressByUser = new();

    /// <summary>
    /// Loads progress after persistent preferences have been loaded. A missing row therefore belongs to a
    /// pre-onboarding account and is deliberately migrated to the completed state rather than locked out.
    /// </summary>
    public async Task<Wh40kPlayerProgressSnapshot> LoadForExistingPreferencesAsync(
        NetUserId userId,
        CancellationToken cancel)
    {
        var progress = await _db.GetOrCreateWh40kPlayerProgressAsync(
            userId,
            Wh40kPlayerProgressSnapshot.LegacyCompleted,
            cancel);
        _progressByUser[userId] = progress;
        return progress;
    }

    public void SetTransient(NetUserId userId, Wh40kPlayerProgressSnapshot progress)
    {
        _progressByUser[userId] = progress;
    }

    public Wh40kPlayerProgressSnapshot Get(NetUserId userId)
    {
        return _progressByUser.GetValueOrDefault(userId, Wh40kPlayerProgressSnapshot.Unknown);
    }

    public void Remove(NetUserId userId)
    {
        _progressByUser.Remove(userId);
    }
}
