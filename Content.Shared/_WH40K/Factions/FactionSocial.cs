using System.Linq;
using Content.Shared._Mono.Company;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Factions;

public static class FactionSocialRules
{
    public const int MaxMessageLength = 400;
    public const int MaxTitleLength = 64;
    public const int MaxAnnouncementLength = 1600;
    public const int MaxReasonLength = 240;
    public const int AnnouncementLimit = 8;
    public const int MessageLimit = 50;
    public const int MaxRelationAdjustment = 10;
    public static readonly TimeSpan RelationAdjustmentInterval = TimeSpan.FromHours(1);
    public static bool ValidRelationAdjustment(int delta) => delta is >= -MaxRelationAdjustment and <= MaxRelationAdjustment && delta != 0;
    public static int AdjustRelation(int score, int delta) => Math.Clamp(score + delta, -100, 100);
    public static int RelationCooldownSeconds(DateTime availableAt, DateTime now) =>
        (int) Math.Clamp(Math.Ceiling((availableAt - now).TotalSeconds), 0, int.MaxValue);
    public static bool CanPublish(FactionRole? role) => role is FactionRole.Leader or FactionRole.Officer;
    public static bool CanSetRelations(FactionRole? role) => role == FactionRole.Leader;
    public static (string Source, string Target) RelationPair(string first, string second) =>
        string.CompareOrdinal(first, second) < 0 ? (first, second) : (second, first);
    public static bool ValidText(string? text, int limit, bool multiline = false) => text != null &&
        text.Length <= limit && !string.IsNullOrWhiteSpace(text) &&
        !text.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t') ||
                      c is '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' or '\u2066' or '\u2067' or '\u2068' or '\u2069');
    public static string StatusKey(int score) => score switch
    {
        <= -60 => "wh40k-faction-rel-enemy",
        < -20 => "wh40k-faction-rel-hostile",
        <= 20 => "wh40k-faction-rel-neutral",
        < 60 => "wh40k-faction-rel-friendly",
        _ => "wh40k-faction-rel-ally",
    };
}

[Serializable, NetSerializable]
public sealed record FactionAnnouncement(Guid Id, string Author, string Title, string Text, DateTime CreatedAt);

[Serializable, NetSerializable]
public sealed record FactionTransmission(Guid Id, string Author, string Text, DateTime CreatedAt, bool Distress = false, string Location = "");

[Serializable, NetSerializable]
// Source and Target identify one shared pair, stored in ordinal prototype ID order.
public sealed record FactionRelation(string Source, string Target, int Score = 0, string Reason = "", int Revision = 0,
    string Author = "", DateTime UpdatedAt = default);

[Serializable, NetSerializable]
public sealed record FactionSocialSnapshot(FactionAnnouncement[] Announcements, FactionTransmission[] Messages,
    bool CanTransmit, int MessageCooldown = 0, int DistressCooldown = 0);

[Serializable, NetSerializable]
public sealed record FactionSocialRequest(Guid Id = default, string Text = "", string Title = "", string Target = "",
    int Score = 0, int Revision = 0, int Delta = 0);
