using System.Collections.Concurrent;
using Robust.Shared.Network;

namespace Content.Server._Mono.MonoCoins;

public sealed partial class MonoCoinsManager
{
    [Dependency] private IEntityManager _entities = default!;
    private readonly ConcurrentDictionary<NetUserId, int> _balanceGenerations = new();

    public void InvalidateCachedBalance(NetUserId userId)
    {
        _balanceGenerations.AddOrUpdate(userId, 1, (_, generation) => generation + 1);
        _cachedBalance.TryRemove(userId, out _);
    }
}
