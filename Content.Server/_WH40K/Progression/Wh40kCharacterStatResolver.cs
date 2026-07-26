using System.Collections.ObjectModel;
using Content.Shared._WH40K.CharacterCreation;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Progression;

public readonly record struct Wh40kCharacteristicBreakdown(
    int CreationAllocation,
    int Homeworld,
    int Origin,
    int Class,
    int LevelPurchases,
    int Talents = 0,
    int Equipment = 0,
    int TemporaryEffects = 0)
{
    public int Final => checked(
        CreationAllocation +
        Homeworld +
        Origin +
        Class +
        LevelPurchases +
        Talents +
        Equipment +
        TemporaryEffects);
}

public sealed record Wh40kResolvedStats(
    IReadOnlyDictionary<Wh40kCharacteristic, Wh40kCharacteristicBreakdown> Breakdown,
    long Revision)
{
    public int GetFinal(Wh40kCharacteristic characteristic)
    {
        return Breakdown.TryGetValue(characteristic, out var value) ? value.Final : 0;
    }
}

/// <summary>
/// Pure permanent-stat resolver over an immutable account foundation, purchases and prototype modifiers.
/// Entity mutation and round-only modifiers belong to the runtime adapter.
/// </summary>
public sealed class Wh40kCharacterStatResolver
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    public Wh40kResolvedStats Resolve(Wh40kAccountRpgRecord account)
    {
        if (!_prototypes.TryIndex<Wh40kHomeworldPrototype>(
                account.Foundation.HomeworldId,
                out var homeworld) ||
            !_prototypes.TryIndex<Wh40kOriginPrototype>(
                account.Foundation.OriginId,
                out var origin) ||
            !_prototypes.TryIndex<Wh40kCharacterClassPrototype>(
                account.Foundation.ClassId,
                out var characterClass))
        {
            throw new InvalidOperationException(
                $"WH40K RPG account {account.Foundation.UserId} references an unknown foundation prototype.");
        }

        return Resolve(
            account,
            homeworld.CharacteristicModifiers,
            origin.CharacteristicModifiers,
            characterClass.CharacteristicModifiers);
    }

    internal static Wh40kResolvedStats Resolve(
        Wh40kAccountRpgRecord account,
        IReadOnlyDictionary<Wh40kCharacteristic, int> homeworldModifiers,
        IReadOnlyDictionary<Wh40kCharacteristic, int> originModifiers,
        IReadOnlyDictionary<Wh40kCharacteristic, int> classModifiers)
    {
        var breakdown = new Dictionary<Wh40kCharacteristic, Wh40kCharacteristicBreakdown>();
        foreach (var characteristic in Enum.GetValues<Wh40kCharacteristic>())
        {
            var levelPurchases = account.AttributePurchases.TryGetValue(characteristic, out var purchase)
                ? purchase.PurchasedPoints
                : 0;
            var value = new Wh40kCharacteristicBreakdown(
                account.Foundation.InitialCharacteristicPoints.GetValueOrDefault(characteristic),
                homeworldModifiers.GetValueOrDefault(characteristic),
                originModifiers.GetValueOrDefault(characteristic),
                classModifiers.GetValueOrDefault(characteristic),
                levelPurchases);

            _ = value.Final;
            breakdown.Add(characteristic, value);
        }

        return new Wh40kResolvedStats(
            new ReadOnlyDictionary<Wh40kCharacteristic, Wh40kCharacteristicBreakdown>(breakdown),
            account.Progress.Revision);
    }
}
