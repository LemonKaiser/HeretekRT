using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WH40K.CharacterCreation;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Progression;

/// <summary>
/// Owns the immutable account foundation cache and performs the one-time legacy data migration.
/// Mutable progression is owned separately by <see cref="Wh40kProgressManager"/>.
/// </summary>
public sealed class Wh40kAccountRpgManager
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly Dictionary<NetUserId, Wh40kRpgFoundationRecord> _foundations = new();

    public async Task<Wh40kAccountRpgRecord?> LoadForExistingPreferencesAsync(
        NetUserId userId,
        Wh40kPlayerProgressSnapshot onboarding,
        CancellationToken cancel)
    {
        if (!onboarding.CanUseLegacyPersonalization)
        {
            _foundations.Remove(userId);
            return null;
        }

        var account = await _db.GetWh40kAccountRpgAsync(userId, cancel);
        if (account == null)
        {
            account = await _db.GetOrCreateWh40kAccountRpgAsync(
                userId,
                RollLegacyFoundation(),
                cancel);
        }

        _foundations[userId] = account.Foundation;
        return account;
    }

    public void CacheCompletedOnboarding(NetUserId userId, Wh40kCharacterBuild build)
    {
        if (!build.IsCompleteFoundation)
            throw new ArgumentException("Cannot cache an incomplete WH40K RPG foundation.", nameof(build));

        _foundations[userId] = new Wh40kRpgFoundationRecord(
            userId,
            build.HomeworldId!,
            build.OriginId!,
            build.ClassId!,
            build.PortraitId!,
            new Dictionary<Wh40kCharacteristic, int>(build.CharacteristicPoints),
            Wh40kRpgFoundationSource.Onboarding,
            DateTime.UtcNow);
    }

    public Wh40kAccountRpgRecord CreateTransientLegacyAccount(NetUserId userId)
    {
        var draft = RollLegacyFoundation();
        var now = DateTime.UtcNow;
        var foundation = new Wh40kRpgFoundationRecord(
            userId,
            draft.HomeworldId,
            draft.OriginId,
            draft.ClassId,
            draft.InitialPortraitId,
            new Dictionary<Wh40kCharacteristic, int>(draft.InitialCharacteristicPoints),
            draft.Source,
            now);
        var progress = new Wh40kRpgProgressRecord(
            userId,
            Wh40kExperienceCurve.ProgressSchemaVersion,
            0,
            Wh40kExperienceCurve.MinimumLevel,
            0,
            now,
            now,
            0);
        var account = new Wh40kAccountRpgRecord(
            foundation,
            progress,
            new Dictionary<Wh40kCharacteristic, Wh40kAttributePurchaseRecord>());
        _foundations[userId] = foundation;
        return account;
    }

    public bool TryGetFoundationBuild(NetUserId userId, out Wh40kCharacterBuild build)
    {
        if (_foundations.TryGetValue(userId, out var foundation))
        {
            build = foundation.ToCharacterBuild();
            return true;
        }

        build = default!;
        return false;
    }

    public void Remove(NetUserId userId)
    {
        _foundations.Remove(userId);
    }

    private Wh40kRpgFoundationDraft RollLegacyFoundation()
    {
        var homeworlds = _prototypes.EnumeratePrototypes<Wh40kHomeworldPrototype>()
            .OrderBy(prototype => prototype.Order)
            .ThenBy(prototype => prototype.ID)
            .ToArray();
        var origins = _prototypes.EnumeratePrototypes<Wh40kOriginPrototype>()
            .OrderBy(prototype => prototype.Order)
            .ThenBy(prototype => prototype.ID)
            .ToArray();
        var classes = _prototypes.EnumeratePrototypes<Wh40kCharacterClassPrototype>()
            .OrderBy(prototype => prototype.Order)
            .ThenBy(prototype => prototype.ID)
            .ToArray();
        var portraits = _prototypes.EnumeratePrototypes<Wh40kPortraitPrototype>()
            .OrderBy(prototype => prototype.Order)
            .ThenBy(prototype => prototype.ID)
            .ToArray();

        if (homeworlds.Length == 0 || origins.Length == 0 || classes.Length == 0 || portraits.Length == 0)
            throw new InvalidOperationException("WH40K legacy foundation cannot be rolled without complete prototypes.");

        return new Wh40kRpgFoundationDraft(
            homeworlds[_random.Next(homeworlds.Length)].ID,
            origins[_random.Next(origins.Length)].ID,
            classes[_random.Next(classes.Length)].ID,
            portraits[_random.Next(portraits.Length)].ID,
            RollInitialCharacteristicPoints(_random.Next),
            Wh40kRpgFoundationSource.LegacyRandom);
    }

    internal static IReadOnlyDictionary<Wh40kCharacteristic, int> RollInitialCharacteristicPoints(
        Func<int, int> next)
    {
        var characteristics = Enum.GetValues<Wh40kCharacteristic>();
        var result = new Dictionary<Wh40kCharacteristic, int>();

        for (var point = 0; point < Wh40kCharacterBuild.MaximumAttributePoints; point++)
        {
            var index = next(characteristics.Length);
            if (index < 0 || index >= characteristics.Length)
                throw new ArgumentOutOfRangeException(nameof(next), "Random characteristic index is out of range.");

            var characteristic = characteristics[index];
            result[characteristic] = result.GetValueOrDefault(characteristic) + 1;
        }

        return result;
    }
}
