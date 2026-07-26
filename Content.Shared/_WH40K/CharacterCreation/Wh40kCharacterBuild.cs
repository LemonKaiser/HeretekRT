using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.CharacterCreation;

/// <summary>
/// Server-authoritative progress of the account through the introductory WH40K story.
/// </summary>
public enum Wh40kActStage : byte
{
    Act1NotStarted,
    Act1InProgress,
    Act1Completed,
}

/// <summary>
/// Separates creating the first character from completing Act 1 itself.
/// </summary>
public enum Wh40kOnboardingStatus : byte
{
    Unknown,
    Required,
    CharacterCreated,
}

/// <summary>
/// A small account-state snapshot sent with preferences before the lobby opens.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct Wh40kPlayerProgressSnapshot(
    Wh40kActStage ActStage,
    Wh40kOnboardingStatus OnboardingStatus,
    int OnboardingProfileSlot)
{
    public static readonly Wh40kPlayerProgressSnapshot Unknown = new(
        Wh40kActStage.Act1NotStarted,
        Wh40kOnboardingStatus.Unknown,
        -1);

    /// <summary>
    /// Used for existing preferences created before the onboarding system, and for non-persistent guests.
    /// </summary>
    public static readonly Wh40kPlayerProgressSnapshot LegacyCompleted = new(
        Wh40kActStage.Act1Completed,
        Wh40kOnboardingStatus.CharacterCreated,
        -1);

    public bool IsKnown => OnboardingStatus != Wh40kOnboardingStatus.Unknown;

    /// <summary>
    ///     The regular lobby and late-join flow become available as soon as the first profile has been created.
    ///     Act I progression is intentionally independent from that profile-creation gate.
    /// </summary>
    public bool CanUseLegacyPersonalization => OnboardingStatus == Wh40kOnboardingStatus.CharacterCreated;
}

/// <summary>
/// The mechanical-neutral result of the introductory character creation.
/// Gameplay systems deliberately do not consume these values yet.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class Wh40kCharacterBuild : IEquatable<Wh40kCharacterBuild>
{
    public const int MaximumAttributePoints = 10;
    private const int MaximumIdLength = 64;

    [DataField]
    public string? HomeworldId { get; set; }

    [DataField]
    public string? OriginId { get; set; }

    [DataField]
    public string? ClassId { get; set; }

    [DataField]
    public string? PortraitId { get; set; }

    [DataField]
    public Dictionary<Wh40kCharacteristic, int> CharacteristicPoints { get; set; } = new();

    public long AllocatedCharacteristicPoints => CharacteristicPoints.Values.Sum(value => (long) value);

    public bool IsCompleteFoundation =>
        HomeworldId is not null &&
        OriginId is not null &&
        ClassId is not null &&
        PortraitId is not null &&
        AllocatedCharacteristicPoints == MaximumAttributePoints &&
        Equals(Validated());

    public Wh40kCharacterBuild Clone()
    {
        return new Wh40kCharacterBuild
        {
            HomeworldId = HomeworldId,
            OriginId = OriginId,
            ClassId = ClassId,
            PortraitId = PortraitId,
            CharacteristicPoints = new Dictionary<Wh40kCharacteristic, int>(CharacteristicPoints),
        };
    }

    /// <summary>
    ///     Returns the onboarding total: points allocated by the player plus selected homeworld, origin and class modifiers.
    ///     Modifiers remain derived from prototype data so changing a selection never mutates allocated points.
    /// </summary>
    public int GetCharacteristicTotal(
        Wh40kCharacteristic characteristic,
        Wh40kHomeworldPrototype? homeworld,
        Wh40kOriginPrototype? origin,
        Wh40kCharacterClassPrototype? characterClass)
    {
        return CharacteristicPoints.GetValueOrDefault(characteristic) +
               (homeworld?.GetCharacteristicModifier(characteristic) ?? 0) +
               (origin?.GetCharacteristicModifier(characteristic) ?? 0) +
               (characterClass?.GetCharacteristicModifier(characteristic) ?? 0);
    }

    public Wh40kCharacterBuild Validated()
    {
        var result = new Wh40kCharacterBuild
        {
            HomeworldId = ValidateId(HomeworldId),
            OriginId = ValidateId(OriginId),
            ClassId = ValidateId(ClassId),
            PortraitId = ValidateId(PortraitId),
        };

        var remainingPoints = MaximumAttributePoints;
        foreach (var characteristic in Enum.GetValues<Wh40kCharacteristic>())
        {
            if (!CharacteristicPoints.TryGetValue(characteristic, out var requestedPoints))
                continue;

            var points = Math.Min(Math.Clamp(requestedPoints, 0, MaximumAttributePoints), remainingPoints);
            if (points <= 0)
                continue;

            result.CharacteristicPoints[characteristic] = points;
            remainingPoints -= points;
        }

        return result;
    }

    public bool Equals(Wh40kCharacterBuild? other)
    {
        if (other is null)
            return false;

        return HomeworldId == other.HomeworldId
               && OriginId == other.OriginId
               && ClassId == other.ClassId
               && PortraitId == other.PortraitId
               && CharacteristicPoints.OrderBy(pair => pair.Key).SequenceEqual(other.CharacteristicPoints.OrderBy(pair => pair.Key));
    }

    public override bool Equals(object? obj)
    {
        return obj is Wh40kCharacterBuild other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(HomeworldId);
        hash.Add(OriginId);
        hash.Add(ClassId);
        hash.Add(PortraitId);

        foreach (var pair in CharacteristicPoints.OrderBy(pair => pair.Key))
        {
            hash.Add(pair.Key);
            hash.Add(pair.Value);
        }

        return hash.ToHashCode();
    }

    private static string? ValidateId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        id = id.Trim();
        if (id.Length > MaximumIdLength || id.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            return null;

        return id;
    }
}

/// <summary>
/// The five presentation-only characteristics selected in the onboarding UI.
/// </summary>
public enum Wh40kCharacteristic : byte
{
    Melee,
    Ranged,
    Endurance,
    Intelligence,
    Agility,
}
