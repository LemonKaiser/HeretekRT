using System.Linq;
using Content.Shared.FixedPoint;
using Content.Shared._WH40K.Progression;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Pure arithmetic and short-lived anti-farm policy shared by Stage 3 adapters.
/// </summary>
public static class Wh40kExperiencePolicy
{
    public const float DefaultParticipationRadius = 100f;
    public const int MultiplierScale = 1000;
    public const int FullMultiplier = 1000;
    public const int HalfMultiplier = 500;
    public const int ZeroMultiplier = 0;

    public static long ApplyMultiplier(long amountTenths, int multiplier)
    {
        if (amountTenths < 0)
            throw new ArgumentOutOfRangeException(nameof(amountTenths));
        if (multiplier < 0)
            throw new ArgumentOutOfRangeException(nameof(multiplier));

        return checked(amountTenths * multiplier / MultiplierScale);
    }

    public static IReadOnlyDictionary<NetUserId, long> Split(
        long amountTenths,
        IEnumerable<NetUserId> recipients)
    {
        if (amountTenths < 0)
            throw new ArgumentOutOfRangeException(nameof(amountTenths));

        var ordered = recipients
            .Distinct()
            .OrderBy(userId => userId.UserId)
            .ToArray();
        if (ordered.Length == 0)
            return new Dictionary<NetUserId, long>();

        var share = amountTenths / ordered.Length;
        var remainder = amountTenths % ordered.Length;
        var result = new Dictionary<NetUserId, long>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
            result.Add(ordered[index], share + (index < remainder ? 1 : 0));

        return result;
    }

    public static bool ShouldBlockDirectDamage(
        FixedPoint2 totalDamage,
        bool isDirectSource,
        NetUserId? attacker,
        NetUserId target,
        Func<NetUserId, NetUserId, bool> areInSameParty)
    {
        return totalDamage > FixedPoint2.Zero &&
               isDirectSource &&
               attacker is { } attackerUserId &&
               attackerUserId != target &&
               areInSameParty(attackerUserId, target);
    }

    public static bool MatchesParticipation(
        Wh40kParticipationMode mode,
        float? distance,
        bool isOnGrid,
        bool isOnAllowedMap,
        float radius = DefaultParticipationRadius)
    {
        return mode switch
        {
            Wh40kParticipationMode.Radius =>
                distance is { } actualDistance &&
                radius > 0f &&
                actualDistance <= radius,
            Wh40kParticipationMode.Grid => isOnGrid,
            Wh40kParticipationMode.Sector or Wh40kParticipationMode.Expedition => isOnAllowedMap,
            _ => false,
        };
    }
}

public sealed class Wh40kPvpAntiFarmTracker
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly Dictionary<(string AttackingSide, Guid Victim), Queue<DateTime>> _history = new();
    private DateTime _nextCleanup;

    public Wh40kPvpAntiFarmResult Register(string attackingSide, NetUserId victim, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attackingSide);
        Cleanup(now);

        var key = (attackingSide, victim.UserId);
        if (!_history.TryGetValue(key, out var kills))
        {
            kills = new Queue<DateTime>();
            _history.Add(key, kills);
        }

        while (kills.TryPeek(out var timestamp) && now - timestamp >= Window)
            kills.Dequeue();

        kills.Enqueue(now);
        var occurrence = kills.Count;
        var multiplier = occurrence switch
        {
            1 => Wh40kExperiencePolicy.FullMultiplier,
            2 => Wh40kExperiencePolicy.HalfMultiplier,
            _ => Wh40kExperiencePolicy.ZeroMultiplier,
        };

        return new Wh40kPvpAntiFarmResult(occurrence, multiplier);
    }

    private void Cleanup(DateTime now)
    {
        if (now < _nextCleanup)
            return;

        foreach (var (key, kills) in _history.ToArray())
        {
            while (kills.TryPeek(out var timestamp) && now - timestamp >= Window)
                kills.Dequeue();
            if (kills.Count == 0)
                _history.Remove(key);
        }

        _nextCleanup = now + Window;
    }
}

public readonly record struct Wh40kPvpAntiFarmResult(int Occurrence, int Multiplier);

public sealed class Wh40kSupportRewardLimiter
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private readonly Dictionary<(Guid Helper, Guid Target), DateTime> _lastRewards = new();
    private DateTime _nextCleanup;

    public bool TryRegister(NetUserId helper, NetUserId target, DateTime now)
    {
        if (now >= _nextCleanup)
        {
            foreach (var (expiredPair, expiredAt) in _lastRewards.ToArray())
            {
                if (now - expiredAt >= Cooldown)
                    _lastRewards.Remove(expiredPair);
            }

            _nextCleanup = now + Cooldown;
        }

        var key = (helper.UserId, target.UserId);
        if (_lastRewards.TryGetValue(key, out var lastReward) && now - lastReward < Cooldown)
            return false;

        _lastRewards[key] = now;
        return true;
    }
}
