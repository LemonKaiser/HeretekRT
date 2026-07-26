using System.Linq;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared._WH40K.Progression;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Progression;

internal readonly record struct Wh40kPartyMemberPresentation(string Ckey, bool IsOnline);

internal static class Wh40kRpgUiSnapshotFactory
{
    public static Wh40kPlayerProgressUiSnapshot CreatePlayer(
        string characterName,
        Wh40kAccountRpgRecord account,
        Wh40kResolvedStats resolved)
    {
        var level = account.Progress.Level;
        var levelStart = Wh40kExperienceCurve.GetCumulativeExperienceTenths(level);
        var levelSpan = Wh40kExperienceCurve.GetExperienceToNextLevelTenths(level);
        var currentLevelExperience = Math.Max(0, account.Progress.ExperienceTenths - levelStart);
        var experienceToNext = levelSpan == 0
            ? 0
            : Math.Max(0, levelSpan - currentLevelExperience);

        var characteristics = Enum.GetValues<Wh40kCharacteristic>()
            .Select(characteristic =>
            {
                var breakdown = resolved.Breakdown[characteristic];
                return new Wh40kCharacteristicUiSnapshot(
                    characteristic,
                    breakdown.CreationAllocation,
                    breakdown.Homeworld,
                    breakdown.Origin,
                    breakdown.Class,
                    breakdown.LevelPurchases,
                    breakdown.Final);
            })
            .ToList();

        return new Wh40kPlayerProgressUiSnapshot(
            characterName,
            level,
            account.Progress.ExperienceTenths,
            currentLevelExperience,
            experienceToNext,
            levelSpan,
            account.Progress.UnspentDevelopmentPoints,
            account.Progress.Revision,
            account.Foundation.HomeworldId,
            account.Foundation.OriginId,
            account.Foundation.ClassId,
            characteristics);
    }

    public static Wh40kPartyUiSnapshot CreateParty(
        NetUserId viewer,
        Wh40kPartyRecord? party,
        bool invitesAllowed,
        IReadOnlyDictionary<NetUserId, Wh40kPartyMemberPresentation> presentations)
    {
        if (party == null)
        {
            return new Wh40kPartyUiSnapshot(
                null,
                false,
                invitesAllowed,
                0,
                new List<Wh40kPartyMemberUiSnapshot>());
        }

        var members = party.Members
            .OrderByDescending(member => member.UserId == party.LeaderUserId)
            .ThenBy(member => presentations.GetValueOrDefault(member.UserId).Ckey, StringComparer.OrdinalIgnoreCase)
            .Select(member =>
            {
                var presentation = presentations.GetValueOrDefault(member.UserId);
                var ckey = string.IsNullOrWhiteSpace(presentation.Ckey)
                    ? member.UserId.ToString()
                    : presentation.Ckey;
                return new Wh40kPartyMemberUiSnapshot(
                    member.UserId.UserId,
                    ckey,
                    member.UserId == party.LeaderUserId,
                    member.UserId == viewer,
                    presentation.IsOnline);
            })
            .ToList();

        return new Wh40kPartyUiSnapshot(
            party.Id,
            party.LeaderUserId == viewer,
            invitesAllowed,
            party.ExpiresAt.ToUniversalTime().Ticks,
            members);
    }
}
